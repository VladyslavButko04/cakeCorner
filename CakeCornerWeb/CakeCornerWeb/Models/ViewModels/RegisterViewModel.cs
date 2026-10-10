using System.ComponentModel.DataAnnotations;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Електронна пошта обов’язкова")]
    [EmailAddress(ErrorMessage = "Невірний формат електронної пошти")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Ім’я обов’язкове")]
    public string Name { get; set; } = null!;

    [Required(ErrorMessage = "Пароль обов’язковий")]
    [StringLength(100, ErrorMessage = "Пароль повинен містити щонайменше {2} символи.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "Підтвердження паролю обов’язкове")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Паролі не співпадають")]
    public string ConfirmPassword { get; set; } = null!;

    [DataType(DataType.PhoneNumber)]
    [Phone(ErrorMessage = "Невірний формат телефону")]
    public string? Phone { get; set; }
}