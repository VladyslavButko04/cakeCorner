(() => {
    'use strict';
    const slotIds = { Biscuit: 'BiscuitId', Cream: 'CreamId', Filling: 'FillingId', Mousse: 'MousseId' };
    const slotLabels = { Biscuit: 'Бісквіт', Cream: 'Крем', Filling: 'Начинка', Mousse: 'Мус' };
    const money = value => new Intl.NumberFormat('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' грн';
    const guid = () => {
        if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
        const bytes = new Uint8Array(16);
        globalThis.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
        return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
    };
    const node = (tag, className, text) => {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined && text !== null) element.textContent = String(text);
        return element;
    };
    const localImage = source => {
        if (typeof source !== 'string' || !source.startsWith('/') || source.startsWith('//') || source.includes('\\')) return null;
        try {
            const url = new URL(source, location.origin);
            return url.origin === location.origin ? url.href : null;
        } catch { return null; }
    };
    const numberOrNull = input => !input || input.value.trim() === '' ? null : Number(input.value);

    class RecommendationPanel {
        constructor(panel) {
            this.panel = panel;
            this.controls = panel.querySelector('[data-rec-controls]');
            this.status = panel.querySelector('[data-rec-status]');
            this.saveStatus = panel.querySelector('[data-rec-save-status]');
            this.results = panel.querySelector('[data-rec-results]');
            this.warnings = panel.querySelector('[data-rec-warnings]');
            this.flavorInputs = Array.from(panel.querySelectorAll('[data-rec-flavor]'));
            this.allergenInputs = Array.from(panel.querySelectorAll('[data-rec-allergen]'));
            this.sweetness = panel.querySelector('[data-rec-sweetness]');
            this.mode = panel.querySelector('[data-rec-data-mode]');
            this.target = panel.querySelector('[data-rec-target]');
            this.demo = panel.querySelector('[data-rec-demo]');
            this.constructorForm = panel.dataset.context === 'Custom' ? document.getElementById('customCakeForm') : null;
            this.authenticated = panel.dataset.authenticated === 'true';
            this.csrf = this.controls.querySelector('input[name="__RequestVerificationToken"]').value;
            this.available = false;
            this.revision = 0;
            this.querySerial = 0;
            this.pendingTimer = null;
            this.abort = null;
            this.observer = null;
            this.flavorWeights = {};
            this.populateExclusions();
            this.bind();
            this.initialize();
        }

        populateExclusions() {
            const list = this.panel.querySelector('[data-rec-excluded-list]');
            if (!list || !this.constructorForm) return;
            const seen = new Set();
            for (const [type, id] of Object.entries(slotIds)) {
                for (const option of document.getElementById(id)?.options || []) {
                    const value = Number(option.value);
                    if (!Number.isInteger(value) || value <= 0 || seen.has(value)) continue;
                    seen.add(value);
                    const label = node('label', 'recommendation-chip');
                    const input = node('input');
                    input.type = 'checkbox'; input.value = String(value); input.dataset.recExcluded = '';
                    label.append(input, node('span', '', slotLabels[type] + ': ' + option.textContent));
                    list.append(label);
                }
            }
        }

        async json(url, body, options = {}) {
            const response = await fetch(url, {
                credentials: 'same-origin',
                method: body === undefined ? 'GET' : 'POST',
                headers: body === undefined ? { Accept: 'application/json' } : {
                    Accept: 'application/json', 'Content-Type': 'application/json', RequestVerificationToken: this.csrf
                },
                body: body === undefined ? undefined : JSON.stringify(body),
                ...options
            });
            let value;
            try { value = await response.json(); } catch { value = null; }
            if (!response.ok) {
                const error = new Error(value?.message || value?.detail || (response.status === 401 || response.status === 403
                    ? 'Увійдіть до облікового запису, щоб виконати цю дію.' : 'Не вдалося оновити підбір. Спробуйте ще раз.'));
                error.status = response.status;
                throw error;
            }
            return value;
        }

        bind() {
            this.controls.addEventListener('submit', event => { event.preventDefault(); this.schedule(true); });
            this.controls.addEventListener('change', event => {
                this.revision++;
                if (event.target.matches('[data-rec-flavor]')) this.flavorWeights[event.target.value] = 1;
                this.demo.hidden = this.mode.value !== 'Demo';
                this.saveStatus.textContent = '';
                this.schedule();
            });
            this.controls.addEventListener('input', event => {
                if (event.target.matches('input[type=number]')) { this.revision++; this.schedule(); }
            });
            this.panel.querySelector('[data-rec-refresh]').addEventListener('click', () => this.schedule(true));
            this.panel.querySelector('[data-rec-reset]').addEventListener('click', () => {
                this.revision++;
                this.flavorInputs.forEach(input => { input.checked = false; });
                this.allergenInputs.forEach(input => { input.checked = false; });
                this.panel.querySelectorAll('[data-rec-excluded]').forEach(input => { input.checked = false; });
                this.flavorWeights = {};
                this.sweetness.value = '';
                this.panel.querySelectorAll('[data-rec-min-weight],[data-rec-max-weight]').forEach(input => { input.value = ''; });
                this.saveStatus.textContent = 'Побажання для цього підбору скинуто. Збережений профіль не змінено.';
                this.schedule(true);
            });
            this.panel.querySelector('[data-rec-save]')?.addEventListener('click', () => this.savePreferences());
            if (this.constructorForm) {
                const fields = '#BiscuitId,#CreamId,#FillingId,#MousseId,#Request_Weight,#Request_Quantity,#Request_Shape,#Request_GuestCount';
                this.constructorForm.addEventListener('change', event => { if (event.target.matches(fields)) this.schedule(); });
                this.constructorForm.addEventListener('input', event => { if (event.target.matches(fields)) this.schedule(); });
            }
        }

        async initialize() {
            const initialRevision = this.revision;
            try {
                const state = await this.json(this.panel.dataset.stateUrl);
                this.available = state?.available === true;
                if (!this.available) {
                    this.status.textContent = state?.statusMessage || 'Підбір за вподобаннями ще не налаштований. Скористайтеся каталогом або зберіть торт самостійно.';
                    this.controls.hidden = true;
                    return;
                }
                if (this.authenticated) {
                    try {
                        const preferences = await this.json(this.panel.dataset.preferencesUrl);
                        if (this.revision === initialRevision && preferences) this.hydrate(preferences);
                    } catch { /* A profile is optional; the current preference controls remain usable. */ }
                }
                this.demo.hidden = this.mode.value !== 'Demo';
                this.schedule(true);
            } catch {
                this.status.textContent = 'Підбір за вподобаннями тимчасово недоступний. Каталог і конструктор залишаються доступними.';
                this.controls.hidden = true;
            }
        }

        hydrate(preferences) {
            const flavors = preferences.preferredFlavors || {};
            for (const input of this.flavorInputs) {
                const weight = Number(flavors[input.value]);
                input.checked = Number.isFinite(weight) && weight > 0 && weight <= 5;
                if (input.checked) this.flavorWeights[input.value] = weight;
            }
            if (preferences.desiredSweetness !== null && preferences.desiredSweetness !== undefined) {
                const value = Number(preferences.desiredSweetness);
                if (Number.isFinite(value) && value >= 0 && value <= 5) {
                    if (!Array.from(this.sweetness.options).some(option => option.value === String(value))) {
                        this.sweetness.append(new Option('Збережене значення — ' + value + ' із 5', String(value)));
                    }
                    this.sweetness.value = String(value);
                }
            }
            const exclusions = new Set(preferences.excludedAllergens || []);
            this.allergenInputs.forEach(input => { input.checked = exclusions.has(input.value); });
        }

        preferences() {
            return {
                preferredFlavors: Object.fromEntries(this.flavorInputs.filter(input => input.checked).map(input => [input.value, this.flavorWeights[input.value] || 1])),
                desiredSweetness: numberOrNull(this.sweetness),
                excludedAllergens: this.allergenInputs.filter(input => input.checked).map(input => input.value)
            };
        }

        query() {
            const preferences = this.preferences();
            const query = {
                ...preferences,
                mode: 'Ready', dataMode: this.mode.value, lockedIngredients: {},
                excludedIngredientIds: Array.from(this.panel.querySelectorAll('[data-rec-excluded]:checked'), input => Number(input.value)),
                weightKg: 2, quantity: 1, shape: 'Round', topK: 5,
                readyMinWeightKg: numberOrNull(this.panel.querySelector('[data-rec-min-weight]')),
                readyMaxWeightKg: numberOrNull(this.panel.querySelector('[data-rec-max-weight]'))
            };
            if (this.constructorForm) {
                query.targetType = this.target.value || null;
                query.mode = query.targetType ? 'Ingredients' : 'Custom';
                query.weightKg = Number(document.getElementById('Request_Weight').value);
                query.quantity = Number(document.getElementById('Request_Quantity').value);
                query.shape = document.getElementById('Request_Shape').value;
                for (const [type, id] of Object.entries(slotIds)) {
                    const value = Number(document.getElementById(id)?.value);
                    if (value > 0 && type !== query.targetType) query.lockedIngredients[type] = value;
                }
                if (Object.values(query.lockedIngredients).some(id => query.excludedIngredientIds.includes(id))) {
                    throw new Error('Один із вибраних компонентів також виключений із підбору. Змініть склад або приберіть це виключення.');
                }
                if (!Number.isFinite(query.weightKg) || query.weightKg < 0.5 || query.weightKg > 10 ||
                    !Number.isInteger(query.quantity) || query.quantity < 1 || query.quantity > 99) {
                    throw new Error('Вкажіть вагу від 0,5 до 10 кг і кількість від 1 до 99, щоб оновити підбір.');
                }
            }
            const min = query.readyMinWeightKg, max = query.readyMaxWeightKg;
            if ((min !== null && (!Number.isFinite(min) || min <= 0)) || (max !== null && (!Number.isFinite(max) || max <= 0)) ||
                (min !== null && max !== null && min > max)) throw new Error('Перевірте межі ваги: початкова вага має бути не більшою за кінцеву.');
            return query;
        }

        schedule(immediate = false) {
            clearTimeout(this.pendingTimer);
            this.abort?.abort();
            const serial = ++this.querySerial;
            this.observer?.disconnect();
            this.results.replaceChildren();
            this.warnings.replaceChildren();
            if (!this.available) return;
            this.results.setAttribute('aria-busy', 'true');
            this.status.textContent = 'Оновлюємо підбір…';
            this.pendingTimer = setTimeout(() => this.load(serial), immediate ? 0 : 400);
        }

        async load(serial) {
            if (serial !== this.querySerial) return;
            this.abort = new AbortController();
            try {
                const query = this.query();
                const result = await this.json(this.panel.dataset.queryUrl, query, { signal: this.abort.signal });
                if (serial !== this.querySerial) return;
                this.render(result);
            } catch (error) {
                if (serial !== this.querySerial || error.name === 'AbortError') return;
                this.status.textContent = error.message;
                this.results.replaceChildren();
            } finally {
                if (serial === this.querySerial) this.results.setAttribute('aria-busy', 'false');
            }
        }

        async savePreferences() {
            const button = this.panel.querySelector('[data-rec-save]');
            button.disabled = true;
            this.saveStatus.textContent = 'Зберігаємо вподобання…';
            try {
                await this.json(this.panel.dataset.preferencesUrl, this.preferences());
                this.saveStatus.textContent = 'Уподобання збережено. Для іншого замовлення їх можна змінити.';
            } catch (error) { this.saveStatus.textContent = error.message; }
            finally { button.disabled = false; }
        }

        event(result, item, eventType, position, rating, eventKeys) {
            if (!result.requestId || !item.key) return Promise.resolve(false);
            const key = eventType + (rating === undefined ? '' : ':' + rating);
            if (!eventKeys.has(key)) eventKeys.set(key, guid());
            return this.json(this.panel.dataset.eventUrl, {
                eventKey: eventKeys.get(key), requestId: result.requestId, candidateKey: item.key,
                eventType, position: eventType === 'impression' ? (position || null) : null, rating: rating === undefined ? null : rating
            }, { keepalive: eventType === 'detail_view' }).then(() => true).catch(() => false);
        }

        render(result) {
            this.results.replaceChildren();
            this.warnings.replaceChildren();
            const items = Array.isArray(result?.items) ? result.items : [];
            const status = result?.status || 'unavailable';
            const messages = {
                insufficient_metadata: 'Для такого підбору поки недостатньо перевірених даних. Ви можете продовжити вибір у звичайному каталозі.',
                no_eligible_candidates: 'За цими побажаннями й обмеженнями варіантів немає. Ви можете самостійно змінити налаштування.',
                invalid_request: 'Перевірте налаштування підбору.',
                unavailable: 'Підбір тимчасово недоступний. Ви можете обрати торт самостійно.'
            };
            this.status.textContent = items.length ? (this.target?.value ? 'Підказки для вибраного типу компонента' : 'Варіанти за вашими побажаннями') : (messages[status] || 'За цими налаштуваннями варіантів поки немає.');
            for (const warning of new Set(Array.isArray(result?.warnings) ? result.warnings.filter(w => typeof w === 'string') : [])) {
                this.warnings.append(node('p', '', warning));
            }
            if (!items.length) return;
            if (items.every(item => item.strategy === 'stable-fallback' || item.strategy === 'non-personalized' || item.score === null)) {
                this.warnings.append(node('p', '', 'Це неперсоналізований список. Оберіть смаки або солодкість, щоб уточнити підбір.'));
            }
            const impressions = new WeakMap();
            if ('IntersectionObserver' in window) {
                this.observer = new IntersectionObserver(entries => {
                    for (const entry of entries) if (entry.isIntersecting && entry.intersectionRatio >= 0.45) {
                        const info = impressions.get(entry.target);
                        if (info) this.event(result, info.item, 'impression', info.position, undefined, info.keys);
                        this.observer.unobserve(entry.target);
                    }
                }, { threshold: [0.45] });
            }
            items.slice(0, 5).forEach((item, index) => {
                const eventKeys = new Map();
                const card = this.card(result, item, index + 1, eventKeys);
                this.results.append(card);
                impressions.set(card, { item, position: index + 1, keys: eventKeys });
                this.observer?.observe(card);
            });
        }

        card(result, item, position, eventKeys) {
            const card = node('article', 'recommendation-card');
            const imageUrl = localImage(item.imagePath);
            if (imageUrl) {
                const image = node('img', 'recommendation-card__image');
                image.src = imageUrl; image.alt = item.name || 'Торт'; image.loading = 'lazy';
                image.addEventListener('error', () => { image.hidden = true; }, { once: true });
                card.append(image);
            }
            const body = node('div', 'recommendation-card__body');
            body.append(node('span', 'recommendation-card__badge', item.score === null ? 'Варіант для вибору' : 'Рекомендуємо'));
            body.append(node('h3', '', item.name || (item.kind === 'Custom' ? 'Кастомний торт' : 'Варіант')));
            if (item.kind === 'Custom' && item.ingredientNames) {
                const composition = node('ul', 'recommendation-card__composition');
                for (const type of Object.keys(slotIds)) if (typeof item.ingredientNames[type] === 'string') {
                    composition.append(node('li', '', slotLabels[type] + ': ' + item.ingredientNames[type]));
                }
                body.append(composition);
            }
            if (item.unitPrice !== null && item.unitPrice !== undefined && Number.isFinite(Number(item.unitPrice))) {
                body.append(node('p', 'recommendation-card__price', money(Number(item.unitPrice)) + ' за торт'));
                body.append(node('p', 'recommendation-card__context', String(item.weightKg) + ' кг' + (Number(item.quantity) > 1 ? '; разом за ' + item.quantity + ' шт.: ' + money(Number(item.unitPrice) * Number(item.quantity)) : '')));
            }
            if (Array.isArray(item.reasons) && item.reasons.length) {
                const details = node('details');
                details.append(node('summary', '', 'Чому цей варіант?'));
                const reasons = node('ul');
                item.reasons.filter(reason => typeof reason === 'string').forEach(reason => reasons.append(node('li', '', reason)));
                details.append(reasons);
                details.addEventListener('toggle', () => { if (details.open) this.event(result, item, 'detail_view', position, undefined, eventKeys); });
                body.append(details);
            }
            const actions = node('div', 'recommendation-card__actions');
            if (item.kind === 'Ready' && Number.isInteger(item.cakeId) && item.cakeId > 0) {
                const details = node('a', 'btn btn-sm btn-outline-primary', 'Переглянути');
                const url = new URL(this.panel.dataset.detailsUrl, location.origin);
                url.searchParams.set('id', item.cakeId);
                details.href = url.href;
                details.addEventListener('click', () => this.event(result, item, 'detail_view', position, undefined, eventKeys));
                actions.append(details);
            }
            if (item.kind === 'Ingredient' && this.constructorForm) {
                const choose = node('button', 'btn btn-sm btn-primary', 'Обрати компонент');
                choose.type = 'button';
                choose.addEventListener('click', () => this.applyIngredients({ [item.ingredientType]: item.ingredientId }));
                actions.append(choose);
            }
            if (item.kind === 'Custom' && this.constructorForm) {
                const choose = node('button', 'btn btn-sm btn-outline-primary', 'Застосувати склад');
                choose.type = 'button';
                choose.addEventListener('click', () => this.applyIngredients(item.ingredients));
                actions.append(choose);
            }
            if ((item.kind === 'Ready' || item.kind === 'Custom') && result.requestId) {
                if (this.authenticated) actions.append(this.addForm(result, item));
                else {
                    const login = node('a', 'btn btn-sm btn-primary', 'Увійти й замовити');
                    const url = new URL(this.panel.dataset.loginUrl, location.origin);
                    url.searchParams.set('returnUrl', location.pathname + location.search);
                    login.href = url.href; actions.append(login);
                }
            }
            body.append(actions);
            const rating = node('div', 'recommendation-rating');
            rating.append(node('p', '', 'Наскільки варіант відповідає вашим побажанням?'));
            const ratingButtons = node('div', 'recommendation-rating__buttons');
            const labels = ['Не підходить', 'Частково', 'Підходить', 'Дуже підходить'];
            labels.forEach((label, value) => {
                const button = node('button', '', value + ' — ' + label);
                button.type = 'button'; button.setAttribute('aria-pressed', 'false');
                button.addEventListener('click', async () => {
                    const buttons = ratingButtons.querySelectorAll('button');
                    buttons.forEach(b => { b.disabled = true; });
                    const saved = await this.event(result, item, 'explicit_rating', position, value, eventKeys);
                    if (saved) { button.setAttribute('aria-pressed', 'true'); rating.querySelector('p').textContent = 'Дякуємо! Оцінку відповідності збережено.'; }
                    else { buttons.forEach(b => { b.disabled = false; }); rating.querySelector('p').textContent = 'Не вдалося зберегти оцінку. Спробуйте ще раз.'; }
                });
                ratingButtons.append(button);
            });
            if (result.requestId) { rating.append(ratingButtons); body.append(rating); }
            card.append(body);
            return card;
        }

        applyIngredients(ingredients) {
            if (!ingredients || typeof ingredients !== 'object') return;
            const changes = [];
            for (const [type, value] of Object.entries(ingredients)) {
                const select = document.getElementById(slotIds[type]);
                if (!select || !Array.from(select.options).some(option => option.value === String(value))) {
                    this.status.textContent = 'Один із компонентів більше недоступний у формі. Оновіть сторінку.';
                    return;
                }
                changes.push([select, String(value)]);
            }
            for (const [select, value] of changes) {
                select.value = value;
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            if (changes.length === 1 && this.target) this.target.value = '';
            this.schedule(true);
            document.getElementById('customCakeForm')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }

        addForm(result, item) {
            const form = node('form');
            form.action = this.panel.dataset.addUrl; form.method = 'post';
            const fields = { __RequestVerificationToken: this.csrf, requestId: result.requestId, candidateKey: item.key, eventKey: guid() };
            for (const [name, value] of Object.entries(fields)) {
                const input = node('input'); input.type = 'hidden'; input.name = name; input.value = String(value); form.append(input);
            }
            const button = node('button', 'btn btn-sm btn-primary', 'Додати до кошика');
            button.type = 'submit'; form.append(button);
            form.addEventListener('submit', () => {
                if (item.kind === 'Custom' && this.constructorForm) {
                    for (const [name, sourceId] of Object.entries({ inscription: 'Request_Inscription', designNotes: 'Request_DesignNotes', guestCount: 'Request_GuestCount' })) {
                        const value = document.getElementById(sourceId)?.value;
                        if (value !== undefined && value !== '') {
                            const input = node('input'); input.type = 'hidden'; input.name = name; input.value = value; form.append(input);
                        }
                    }
                }
                button.disabled = true;
            });
            return form;
        }
    }

    const initialize = () => document.querySelectorAll('[data-recommendation-panel]').forEach(panel => {
        if (!panel.dataset.recInitialized) { panel.dataset.recInitialized = 'true'; new RecommendationPanel(panel); }
    });
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
    else initialize();
})();
