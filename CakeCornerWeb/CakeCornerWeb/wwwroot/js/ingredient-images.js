document.addEventListener('error', event => {
    const image = event.target;
    if (!(image instanceof HTMLImageElement) || !image.hasAttribute('data-ingredient-image')) return;
    const fallback = image.dataset.fallback;
    if (fallback && image.getAttribute('src') !== fallback) image.src = fallback;
}, true);
