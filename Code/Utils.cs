using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Nancy.Extensions;
using Nancy.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class Utils
{
    public static string? ShowCurrencyValue(decimal? value)
    {
        if (value == null)
            return null;
        return Math.Round(value.Value).ToString("#,##0");
    }
    public static decimal? GetRoundedValue(decimal? value)
    {
        if(value == null) return null;
        return Math.Truncate(value.Value);
    }
    public static string GetAbsoluteUrl(string page, params KeyValuePair<string, string>[] parameters)
    {
        if (page == null)
            page = "";
        else if (page.StartsWith("~/"))
            page = page.Substring(1);
        string url = "https://www.polekala.com" + (page == null ? "" : ((page.StartsWith("/") ? "" : "/") + page));
        if (parameters.Count() > 0)
        {
            url += "/?";
            foreach (var item in parameters)
            {
                url += item.Key + "=" + item.Value + "&";
            }
            url = url.Substring(0, url.Length - 1);
        }
        return url;
    }
    public static string JsonEncode(object item)
    {
        //var convert = JsonConvert.SerializeObject(item, new JsonSerializerSettings
        //{
        //    StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
        //});
        //var result = "";
        //foreach (var character in convert.ToList())
        //{
        //    if (character == '/')
        //        result += "\\";
        //    result += character;

        //}
        //return result;
        JsonEncodedText json = JsonEncodedText.Encode(item?.ToString() ?? "");
        var result = Encoding.UTF8.GetString(json.EncodedUtf8Bytes);
        return result;
    }
    public static string GetRandomCode(int length)
    {
        var str = "";
        var rnd = new Random();
        for (int i = 0; i < length; i++)
        {
            str += rnd.Next(0, 9);
        }
        return str;
    }
    public static string GenerateMessage(string template, params string[] parameters)
    {
        var message = template;
        for (int i = 0; i < parameters.Length; i++)
        {
            message = message.Replace("{" + i + "}", parameters[i]);
        }
        return message;
    }
    public static string ConvertPersianNumberToEnglish(string numberStr)
    {
        var result = "";
        var persian = new char[] { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        foreach (var item in numberStr)
        {
            var t = persian.ToList().IndexOf(item);
            result += t > -1 ? t.ToString() : item.ToString();
        }
        return result;
    }
    public static string ConvertEnglishNumberToPersian(string numberStr)
    {
        var result = "";
        var persian = new char[] { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        foreach (var item in numberStr)
        {
            var index = -1;
            if (int.TryParse(item.ToString(), out index))
                result += persian[index];
            else
                result += item;
        }
        return result;
    }
    public static string GetSiteBaseUrl(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        return $"{request.Scheme}://{request.Host}";
    }
    public static D CloneObject<D>(object source, params string[] excludes) where D : new()
    {
        int index = 0;
        var dest = new D();
        var destProps = typeof(D).GetProperties();
        Type type = source.GetType();
        foreach (var item in type.GetProperties())
        {
            if (excludes != null && excludes.Contains(item.Name))
                continue;
            try
            {
                var props = destProps.Where(p => p.Name == item.Name);

                var val = item.GetValue(source);
                foreach (var prop in props)
                {
                    if (prop.SetMethod != null)
                        prop.SetValue(dest, val);
                }

            }
            catch (Exception)
            {

            }

        }
        return (D)dest;
    }
    public static void CopyProperties<D>(object source, ref D dest, params string[] excludes)
    {
        var destProps = dest.GetType().GetProperties();
        Type type = source.GetType();
        foreach (var item in type.GetProperties())
        {
            if (excludes != null && excludes.Contains(item.Name))
                continue;
            try
            {
                var props = destProps.Where(p => p.Name == item.Name);
                var val = item.GetValue(source);
                var itemtype = Nullable.GetUnderlyingType(item.PropertyType);
                var despPropType=Nullable.GetUnderlyingType(props.First().PropertyType);
                if ((despPropType != null && despPropType.IsNumeric() || props.First().PropertyType.IsNumeric()) &&
                    (itemtype != null && itemtype.IsEnum || item.PropertyType.IsEnum))
                {
                  
                        val = val == null ? null : (int)val;
                   
                }
                foreach (var prop in props)
                {
                    if (prop.SetMethod != null)
                    {
                        try {
                            prop.SetValue(dest, val);

                        }
                        catch (Exception)
                        {
                            prop.SetValue(dest,decimal.Parse(val.ToString()));

                        }
                    }
                }

            }
            catch (Exception)
            {

            }

        }
    }
    public static string GetImageContent(byte[] image)
    {
        if (image == null || image.Length == 0)
            return "";
        return String.Format("data:image/jpeg;base64,{0}", Convert.ToBase64String(image));
    }
    //public static void UpdateFieldsSave<T>(StudentRegistrationContext context, T entity, params string[] includeProperties)
    //{

    //    var dbEntry = context.Entry(entity);

    //    foreach (var includeProperty in includeProperties)
    //    {
    //        dbEntry.Property(includeProperty).IsModified = true;
    //    }
    //    context.SaveChanges();

    //}
    public static string GetRole(string role)
    {
        switch (role)
        {
            case "military":
                return "نظام وظیفه";
                break;
            case "education":
                return "آموزش";
                break;
            case "research":
                return "پذیرش اولیه";
                break;
            case "admin":
                return "ادمین";
                break;
            default:
                break;
        }
        return null;
    }
    public static HtmlString BeginDialog(string id, string title, bool? large = null)
    {
        large = large == null ? false : true;
        return new HtmlString($@"
<div id='{id}' class='modal  fade' role='dialog'>
  <div class='modal-dialog {(large == true ? "modal-lg" : "")}' role='document'>
    <div class='modal-content'>
      <div class='modal-header'>
        <h6 class='modal-title'>{title}</h5>
 <button type='button' class='close' data-bs-dismiss='modal' aria-label='Close'>
          <span aria-hidden='true'> &times;</span>
               </button>
             </div>
<div class='modal-body'>");
    }

    public static HtmlString EndDialog()
    {
        return new HtmlString($@" </div>
     
    </div>

  </div>
</div>");
    }


    public static float CalculateFileSize(List<IFormFile> files)
    {
        long sum = 0;
        foreach (var item in files)
        {
            sum += item.Length;
        }
        return sum / (1024 * 1024);
    }

    /// <summary>
    /// خواندن فایل کلید خصوصیِ ارسالی و حذف سربرگ و پاورقی PEM.
    /// </summary>
    public static async Task<string> ReadPrivateKeyFile(IFormFile file)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var data = await reader.ReadToEndAsync();

        var result = data
            .Replace("-----BEGIN PRIVATE KEY-----\r\n", "")
            .Replace("\r\n-----END PRIVATE KEY-----", "")
            .TrimEnd('\r', '\n');

        result = result
            .Replace("-----BEGIN PRIVATE KEY-----\n", "")
            .Replace("\n-----END PRIVATE KEY-----", "")
            .TrimEnd('\r', '\n');

        return result;
    }

    public static string DisplayMultilineText(string text)
    {
        var t = (text?.Replace("\r\n", "<br>"));
        return t;
    }
    public static Dictionary<string, string> GetRouteData(string routeValue)
    {
        if (routeValue == null)
            return new Dictionary<string, string>();
        bool val = false;
        var dict = new Dictionary<string, string>();
        string name = "";
        string value = "";
        foreach (var item in routeValue.Split("/"))
        {
            if (item == "")
                continue;
            if (!val)
            {
                name = item;
                dict.Add(name.ToLower(), null);
            }
            else
            {
                value = item;
                dict[name] = value;
            }
            val = !val;
        }
        return dict;
    }


    public static string GetUrl(string relativePath, Dictionary<string, string> routeValues)
    {
        var url = "https://www.polekala.com/" + relativePath.Replace("/", "");
        foreach (var item in routeValues)
        {
            if (string.IsNullOrEmpty(item.Value))
                continue;
            url += $"/{item.Key}/{item.Value}";
        }
        return url;
    }
    public static string GetFormatedCurrency(double price)
    {
        price = (int)(price / 1000)*1000;
        string p = price < 1000 ? price.ToString() : Convert.ToDecimal(price).ToString("#,###");
        return p;
    }
    public static string GetCurrency(double price)
    {
        string p = price < 1000 ? price.ToString() : Convert.ToDecimal(price).ToString("#,###");
        return p;
    }

   public static List<KeyValuePair<string,string>> GetResponseErrors(string response)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        dynamic dic = serializer.DeserializeObject(response);
        var errors = dic.error;
        var errorList=new List<KeyValuePair<string,string>>();
        foreach (var item in errors)
        {
            errorList.Add(new KeyValuePair<string, string>(item.code, item.message));
        }
        return errorList;   
    }

}
