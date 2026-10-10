using System.ComponentModel.DataAnnotations;

public class LoginViewModel
{
    [Required(ErrorMessage = "Електронна пошта обов’язкова")]
    [EmailAddress(ErrorMessage = "Неправильний формат пошти")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Пароль обов’язковий")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = null!;
}