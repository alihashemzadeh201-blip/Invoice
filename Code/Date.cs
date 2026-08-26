using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

public class MyDate
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public string Time { get; set; }
    static public MyDate? GetPersianDate(Nullable<DateTime> date)
    {
        if (date == null)
            return null;
        MyDate dt = new MyDate();
        try
        {
            PersianCalendar pc = new PersianCalendar();
            
            dt.Year = pc.GetYear((DateTime)date);
            dt.Month = pc.GetMonth((DateTime)date);
            dt.Day = pc.GetDayOfMonth((DateTime)date);
            dt.Time = ((DateTime)date).ToShortTimeString();
        }
        catch (Exception ex)
        {
            return null;
        }
        return dt;
    }

    public static string GetPersianDateCode(DateTime? date)
    {
      
        if (date == null)
            return "";
        var d = GetPersianDate(date);
        var shrt = d.ToShortDateString().Replace("/", "").Substring(2);

        return shrt;
    }

    static public DateTime? GetEnglishDate(string date)
    {
        if (string.IsNullOrEmpty(date))
            return null;
        date = Utils.ConvertPersianNumberToEnglish(date);
        PersianCalendar pc = new PersianCalendar();
        MyDate dt;
        try
        {
            dt = Parse(date);
        }
        catch (Exception)
        {

            dt = new MyDate { Year = 1300, Month = 01, Day = 01 };
        }
        TimeSpan time = new TimeSpan();
        TimeSpan.TryParse(dt.Time, out time);
        return pc.ToDateTime(dt.Year, dt.Month, dt.Day, time.Hours, time.Minutes, time.Seconds, 0);
    }

    static public MyDate Parse(string date)
    {
        if (date == null)
            return null;
        string[] parts = date.Split(' ');
        string[] dateItems = parts[0].Split('/');
        MyDate dt = new MyDate();
        dt.Year = int.Parse(dateItems[0]);
        dt.Month = int.Parse(dateItems[1]);
        dt.Day = int.Parse(dateItems[2]);

        if (parts.Count() > 1)
            dt.Time = parts[1];
        return dt;
    }

    public override string ToString()
    {
        
        if (Day == 0 || Month == 0 || Year == 0)
            return "";
        return string.Format("{0}/{1:0#}/{2:0#} {3}", Year, Month, Day, Time);
    }
    public string ToShortDatePersianString()
    {
        if (Day == 0 || Month == 0 || Year == 0)
            return "";
        return Utils.ConvertEnglishNumberToPersian(string.Format("{0}/{1:0#}/{2:0#}", Year, Month, Day));
    }
    public string ToShortDateString()
    {
        if (Day == 0 || Month == 0 || Year == 0)
            return "";
        return string.Format("{0}/{1:0#}/{2:0#}", Year, Month, Day);
    }
    public string ToLongDateString()
    {

        var date = GetEnglishDate(ToShortDatePersianString());
        var day = (DayOfPersianWeek)date.Value.DayOfWeek;
        var month = (PersianMonths)Month;
        return day + " " + Day + " " + month.ToString() + " " + Year + " " + Time;
    }
    public static string GetShorTimeString(DateTime date)
    {
        return date.Hour.ToString("0#") + ":" + date.Minute.ToString("0#");
    }

    public static string  GetDisplayTime(DateTime? date)
    {
        if (date == null)
            return "";
        var diff = DateTime.Now - date.Value;
        if (diff.Days > 0)
            return diff.Days + " روز پیش";
        else if (diff.Hours > 0)
            return diff.Hours + " ساعت پیش";
        else if (diff.Minutes > 0)
            return diff.Minutes + " دقیقه پیش";
        else
            return diff.Seconds + " ثانیه پیش";
    }

    public DayOfPersianWeek DayOfWeek
    {
        get
        {
            DateTime? date = GetEnglishDate(ToShortDatePersianString());
            return (DayOfPersianWeek)(int)date.Value.DayOfWeek;
        }
    }

    public static string GetStandardDateFormat(DateTime date, int hour, int minute, int second)
    {
        return string.Format("{0}-{1:0#}-{2:0#} {3:0#}:{4:0#}:{5:0#}", date.Year, date.Month, date.Day, hour, minute, second);
    }

    public enum DayOfPersianWeek
    {
        یکشنبه,
        دوشنبه,
        سه_شنبه,
        چهارشنبه,
        پنجشنبه,
        جمعه,
        شنبه,
    }

    public enum PersianMonths
    {
        فروردین = 1,
        اردیبهشت = 2,
        خرداد = 3,
        تیر = 4,
        مرداد = 5,
        شهریور = 6,
        مهر = 7,
        آبان = 8,
        آذر = 9,
        دی = 10,
        بهمن = 11,
        اسفند = 12
    }


    public static bool operator >=(MyDate a, MyDate b)
    {
       if (a == null || b == null)
            return false;
        DateTime? ad = a == null ? null : GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd = b == null ? null : GetEnglishDate(b.ToShortDatePersianString());
        if (ad >= bd)
            return true;
        return false;
    }

    public static bool operator <=(MyDate a, MyDate b)
    {
      if (a == null || b == null)
            return false;
        DateTime? ad = a == null ? null : GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd = b == null ? null : GetEnglishDate(b.ToShortDatePersianString());
        if (ad <= bd)
            return true;
        return false;
    }
    public static bool operator ==(MyDate a, MyDate b)
    {

        if (a == null && b == null)
            return true;
        else if (a == null || b == null)
            return false;
        DateTime? ad =a==null?null: GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd =b==null?null: GetEnglishDate(b.ToShortDatePersianString());
        if (ad == bd)
            return true;
        return false;
    }
    public static bool operator >(MyDate a, MyDate b)
    {
       if (a == null || b == null)
            return false;
        DateTime? ad = a == null ? null : GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd = b == null ? null : GetEnglishDate(b.ToShortDatePersianString());
        if (ad > bd)
            return true;
        return false;
    }
    public static bool operator <(MyDate a, MyDate b)
    {
       if (a == null || b == null)
            return false;
        DateTime? ad = a == null ? null : GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd = b == null ? null : GetEnglishDate(b.ToShortDatePersianString());
        if (ad < bd)
            return true;
        return false;
    }
    public static bool operator !=(MyDate a, MyDate b)
    {
        if (a == null && b == null)
            return false;
        else if (a == null || b == null)
            return true;
        DateTime? ad = a == null ? null : GetEnglishDate(a.ToShortDatePersianString());
        DateTime? bd = b == null ? null : GetEnglishDate(b.ToShortDatePersianString());
        if (ad != bd)
            return true;
        return false;
    }

}
