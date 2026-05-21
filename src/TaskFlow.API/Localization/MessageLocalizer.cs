using System.Globalization;

namespace TaskFlow.API.Localization;

// Lightweight key-based localizer for API/business messages (en + ar).
// Resolves the active culture from request localization (Accept-Language or ?culture=).
public interface IMessageLocalizer
{
    string this[string key] { get; }
    string Get(string key);
}

public class MessageLocalizer : IMessageLocalizer
{
    private static readonly Dictionary<string, Dictionary<string, string>> Messages = new()
    {
        ["en"] = new()
        {
            ["auth.email_taken"] = "An account with this email already exists.",
            ["auth.invalid_credentials"] = "Invalid email or password.",
            ["auth.account_disabled"] = "This account is disabled.",
            ["auth.invalid_refresh_token"] = "The refresh token is invalid or expired.",
            ["auth.invalid_reset_token"] = "The password reset token is invalid or expired.",
            ["auth.invalid_invitation"] = "This invitation is invalid or has expired.",
            ["error.validation"] = "One or more validation errors occurred.",
            ["error.unauthorized"] = "You are not authorized to perform this action.",
            ["error.not_found"] = "The requested resource was not found.",
            ["error.conflict"] = "The request conflicts with the current state.",
            ["error.server"] = "An unexpected error occurred. Please try again.",
        },
        ["ar"] = new()
        {
            ["auth.email_taken"] = "يوجد حساب مسجّل بهذا البريد الإلكتروني بالفعل.",
            ["auth.invalid_credentials"] = "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
            ["auth.account_disabled"] = "تم تعطيل هذا الحساب.",
            ["auth.invalid_refresh_token"] = "رمز التحديث غير صالح أو منتهي الصلاحية.",
            ["auth.invalid_reset_token"] = "رمز إعادة تعيين كلمة المرور غير صالح أو منتهي الصلاحية.",
            ["auth.invalid_invitation"] = "هذه الدعوة غير صالحة أو منتهية الصلاحية.",
            ["error.validation"] = "حدث خطأ أو أكثر في التحقق من البيانات.",
            ["error.unauthorized"] = "ليست لديك صلاحية لتنفيذ هذا الإجراء.",
            ["error.not_found"] = "لم يتم العثور على المورد المطلوب.",
            ["error.conflict"] = "الطلب يتعارض مع الحالة الحالية.",
            ["error.server"] = "حدث خطأ غير متوقع. يرجى المحاولة مرة أخرى.",
        }
    };

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (!Messages.TryGetValue(lang, out var table)) table = Messages["en"];
        return table.TryGetValue(key, out var value) ? value : key;
    }
}
