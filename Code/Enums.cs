using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlTypes;
using System.Text.RegularExpressions;

public static class Messages
{
    public const string Required = "فیلد {0} بایستی وارد شود.";
    public const string BetweenLength = "طول فیلد {0} بایستی حداقل {2} و حداکثر {1} کاراکتر باشد.";
    public const string MaximumLength = "طول فیلد {0} بایستی حداکثر {1} کاراکتر باشد.";
    public const string MinimumRange = "فیلد {0} بایستی عددی و حداقل {1} باشد";
    public const string MaximumRange = "فیلد {0} بایستی عددی و حداکثر {1} باشد";
    public const string BetweenRange = "فیلد {0} بایستی عددی ، حداقل {1} و حداکثر {2} باشد";
    public const string InvalidUsername = "نام کاربری و یا رمز عبور اشتباه است.";
    public const string SaveError = "خطا در ذخیره اطلاعات.";
    public const string DeleteError = "خطا در حذف اطلاعات";
    public const string FailedConnect = "خطا در اتصال به پایگاه داده ها";
    public const string DeleteConfirm = "آیا مطمئید که میخواهید این گزینه را حذف کنید؟";
    public const string PasswordConfirm = "رمز عبور با تایید رمز عبور مطابقت ندارد.";
    public const string Invalid = "{0} نامعتبر است";
    public const string Numeric = "فیلد {0} بایستی عددی باشد.";
    public const string PhoneNumber = "تلفن همراه وارد شده نامعتبر است";
    public const string MaxUploadSize = "حجم فایل های ارسالی نباید بیش از {0} مگابایت باشد";
    public const string EnglishRequired = "{0} is required";
    public const string EnglishPhoneNumber = "Mobile number is not valid";
    public const string EnglishInvalid = "{0} is invalid";
    public const string EnglishBetweenLength = "{0} length must be between {2} and {1} characters";
    public const string EmailAddress = "Email address is not valid";
}
public enum StuffType
{
    شناسه_عمومی_تولید_داخل_کالا,
    شناسه_اختصاصی_تولید_داخل_کالا,
    شناسه_عمومی_وارداتی_کالا,
    شناسه_اختصاصی_وارداتی_کالا,
    شناسه_عمومی_تولید_داخل_خدمت,
    شناسه_اختصاصی_تولید_داخل_خدمت,
    شناسه_عمومی_وارداتی_خدمت,
    شناسه_اختصاصی_وارداتی_خدمت,

}
public enum InvoiceType
{
    نوع_اول = 1,
    نوع_دوم = 2,
}
public enum InvoicePattern
{
    فروش = 1,
    فروش_ارز = 2,
    طلا_و_جواهر = 3,
    قرارداد_پیمانکاری = 4,
    قبوض_خدماتی = 5,
    بلیط_هواپیما = 6,
    صادرات = 7
}

public enum InvoiceTopic
{
    اصلی = 1,
    اصلاحی = 2,
    ابطالی = 3,
    برگشت_از_فروش = 4
}

public enum PersonType
{
    حقیقی = 1,
    حقوقی = 2,
    مشارکت_مدنی = 3,
    اتباع_غیر_ایرانی = 4,
}

public enum CheckoutType
{
    نقدی = 1,
    نسیه = 2,
    نقد_نسیه = 3
}

public enum FlightType
{
    داخلی = 1,
    خارجی = 2
}

public enum PayType
{
    چک = 1,
    تهاتر = 2,
    وجه_نقد = 3,
    پوز = 4,
    درگاه_اینترنتی = 5,
    کارت_به_کارت = 6,
    انتقال_به_حساب = 7,
    سایر=8

}

public enum InvoiceStatus
{
    در_انتظار_ارسال,
    موفق,
    خطا_در_ارسال,
    در_حال_ارسال,
    اشکال_در_صورتحساب,
    پیدا_نشد,
    در_حال_انجام,
    ابطال_شده,
    اصلاح_شده
}

public enum VatRate
{
    معاف=0,
    درصد_9=9,
    درصد_10 = 10,
    درصد_16=16
   
}
public class Dictionaries
{
    public static Dictionary<string,string> TemplateMappings= new Dictionary<string, string>
                                                        {
                                        { "InvoiceCode", "کد فاکتور" },
                                        { "InvoiceDate", "تاریخ صورتحساب" },
                                        { "InvoiceType", "نوع صورتحساب" },
                                        { "InvoicePattern", "الگوی صورتحساب" },
                                        { "PaymentMethod", "روش تسویه" },
                                        { "PersonType", "نوع شخص" },
                                        { "BuyerEconomicCode", "شماره اقتصادی خریدار" },
                                        { "ProductCode", "شناسه کالا" },
                                        { "ProductDescription", "شرح کالا" },
                                        { "Quantity", "تعداد/مقدار" },
                                        { "UnitPrice", "مبلغ واحد" },
                                        { "Discount", "تخفیف" },
                                        { "TaxPercent", "درصد ارزش افزوده" },
                                        { "Unit", "واحد سنجش" }
                                    };
}
public class EnumUtils
{
    public static string GetDisplayText<T>(T item)
    {
        return item.ToString().Replace("_", " ");
    }
    public static List<T> GetEnumItems<T>()
    {
        var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        var names = Enum.GetNames(t);
        var items = Enum.GetValues(t).Cast<T>().ToList();
        return items;
    }
    public static List<string> GetEnumStringList<T>()
    {
        var names = Enum.GetNames(typeof(T));
        int index = 0;
        List<string> list = new List<string>();
        foreach (var item in names)
        {
            list.Add(item);
        }
        return list;
    }

    public static List<SelectListItem> GetEnumSelectList<T>() 
    {
        var t=Nullable.GetUnderlyingType(typeof(T)) ??typeof(T);
        var list= Enum.GetValues(t).Cast<T>().Select(v => new SelectListItem
        {
            Text = v.ToString().Replace("_"," "),
            Value = Convert.ToInt32(v).ToString()
        }).ToList();
        if (Nullable.GetUnderlyingType(typeof(T))!=null) 
            list.Insert(0, new SelectListItem("-------", null));
        return list;
    }
}

public class RequiredIfAttribute : ValidationAttribute
{
    RequiredAttribute _innerAttribute = new RequiredAttribute();
    public string _dependentProperty { get; set; }
    public object _targetValue { get; set; }

    public RequiredIfAttribute(string dependentProperty, object targetValue)
    {
        this._dependentProperty = dependentProperty;
        this._targetValue = targetValue;
    }
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var field = validationContext.ObjectType.GetProperty(_dependentProperty);
        if (field != null)
        {
            var dependentValue = field.GetValue(validationContext.ObjectInstance, null);
            var fieldType = Nullable.GetUnderlyingType(field.PropertyType);
            if (fieldType!=null && fieldType.IsEnum || field.PropertyType.IsEnum)
                dependentValue =dependentValue==null?null: (int)dependentValue;
            if ((dependentValue == null && _targetValue == null) || (_targetValue?.ToString()?.Contains(dependentValue?.ToString()??"@@")??false))
            {
                if (!_innerAttribute.IsValid(value))
                {
                    string name = validationContext.DisplayName;
                    string specificErrorMessage = ErrorMessage;
                    if (specificErrorMessage.Length < 1)
                        specificErrorMessage = $"{name} is required.";

                    return new ValidationResult(specificErrorMessage.Replace("{0}",name), new[] { validationContext.MemberName });
                }
            }
            return ValidationResult.Success;
        }
        else
        {
            return new ValidationResult(FormatErrorMessage(_dependentProperty));
        }
    }
}

