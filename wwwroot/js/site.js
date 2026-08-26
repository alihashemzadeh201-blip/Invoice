// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
$("document").ready(function () {
    $('.currency').on("keyup", function () {
        var persian = wordifyRialsInTomans($(this).val());
        $(this).nextAll('.persian-text').text(persian);
    });
   
    $("input.mask").each((i, ele) => {
        let ele1 = $(ele);
        let clone = ele1.clone(false);

        clone.attr("type", "text");
        clone.attr("inputmode", "numeric");
        clone.addClass("money-mask-display");

        const formatMoneyValue = () => {
            let rawValue = ele1.val() || "";
            rawValue = rawValue.toString().replace(/,/g, "");

            if (rawValue === "") {
                clone.val("");
                return;
            }

            const isNegative = rawValue.startsWith("-");
            let normalized = rawValue.replace(/[^0-9.]/g, "");

            if (normalized === "") {
                clone.val("");
                return;
            }

            const parts = normalized.split(".");
            const integerPart = parts[0].replace(/^0+(?=\d)/, "") || "0";
            const decimalPart = parts.length > 1 ? "." + parts.slice(1).join("") : "";
            const formattedInteger = Number(integerPart).toLocaleString("en-US");

            clone.val((isNegative ? "-" : "") + formattedInteger + decimalPart);
        };

        formatMoneyValue();

        ele1.after(clone);
        ele1.hide();

        clone.on("focus", function () {
            ele1.show();
            ele1.focus();
            clone.hide();
        });

        ele1.on("blur", function () {
            formatMoneyValue();
            clone.show();
            ele1.hide();
        });

        ele1.on("input", function () {
            formatMoneyValue();
        });

        ele1.on("change", function () {
            formatMoneyValue();
        });
    });

    $('[data-val-required][value=""]').addClass('required');
    $('[data-val-required][value=""]').change(function () {
        if ($(this).val() != "") {
            $(this).removeClass('required');
        }
        else if (!$(this).hasClass('required')) {
            $(this).addClass('required');
        }
    });
    try {
        $(".date-picker").persianDatepicker({
            formatDate: 'YYYY/MM/DD'
        });
    }
    catch {

    }
    try {
        $(".search-select").select2({
            tags: false,
            placeholder: "--",
        });
    }
    catch {

    }
    
});

(function () {

    // ==========================================
    // 1. لودینگ اولیه صفحه
    // ==========================================

    // مخفی کردن لودینگ اولیه بعد از بارگذاری کامل
    window.addEventListener('load', function () {
        setTimeout(() => {
            hideNavigationLoader();
        }, 1000);
    });

    // فالبک: اگر بیش از 10 ثانیه طول کشید
    //setTimeout(() => {
    //    showNavigationLoader();
    //}, 10000);

    // ==========================================
    // 2. لودینگ نیویگیشن (بین صفحات)
    // ==========================================

    // نمایش لودینگ برای لینک‌ها
    document.addEventListener('DOMContentLoaded', function () {
        // برای تمام لینک‌ها
        const links = document.querySelectorAll('a[href]:not([target="_blank"]):not([href^="#"]):not(.no-loader)');
        links.forEach(link => {
            link.addEventListener('click', function (e) {
                const href = this.getAttribute('href');
                // فقط برای لینک‌های داخلی
                if (href && !href.startsWith('http') && !href.startsWith('//') && href !== 'javascript:void(0)') {
                    showNavigationLoader();
                }
            });
        });

        // برای فرم‌ها
        //const forms = document.querySelectorAll('form:not(.no-loader)');
        //forms.forEach(form => {
        //    form.addEventListener('submit', function (e) {
        //        if (form.method.toLowerCase() === 'post') {
        //            showNavigationLoader();
        //        }
        //    });
        //});

        //// برای دکمه‌های با data-loader
        //const loaderButtons = document.querySelectorAll('[data-show-loader]');
        //loaderButtons.forEach(btn => {
        //    btn.addEventListener('click', function () {
        //        showNavigationLoader();
        //    });
        //});
    });

    // ==========================================
    // توابع کمکی
    // ==========================================

    function showNavigationLoader() {
    const navigationLoader = document.getElementById('navigationLoader');
        if (navigationLoader) {
            navigationLoader.style.display = 'flex';
            // کمی تاخیر برای اعمال transition
           
        }
    }

    function hideNavigationLoader() {
    const navigationLoader = document.getElementById('navigationLoader');
        if (navigationLoader) {
            setTimeout(() => {
                navigationLoader.style.display = 'none';
            }, 300);
        }
    }

    // مخفی کردن لودینگ اگر برگشت به صفحه قبلی
    window.addEventListener('pageshow', function (event) {
        if (event.persisted) {
            hideNavigationLoader();
        }
    });

    //// Integration با jQuery AJAX
    //if (typeof jQuery !== 'undefined') {
    //    $(document).ajaxStart(function () {
    //        showNavigationLoader();
    //    });

    //    $(document).ajaxStop(function () {
    //        hideNavigationLoader();
    //    });
    //}

    // Export برای استفاده در کدهای دیگر
    window.NavigationLoader = {
        show: showNavigationLoader,
        hide: hideNavigationLoader
    };

})();









function convertToPersianText(number) {
    const persianNumbers = ['صفر', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه'];
    const tens = ['ده', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'];
    const hundreds = ['صد', 'دویست', 'سیصد', 'چهارصد', 'پانصد', 'ششصد', 'هفتصد', 'هشتصد', 'نهصد'];
    const thousands = ['', 'هزار', 'میلیون', 'میلیارد', 'تریلیون']; // Extend as needed

    if (number === 0) {
        return persianNumbers[0]; // Return 'صفر' for zero
    }

    let result = '';
    let i = 0;

    while (number > 0) {
        const part = number % 1000;
        if (part > 0) {
            const text = convertThreeDigitsToText(part);
            result = text + ' ' + thousands[i] + ' ' + result;
        }
        i++;
        number = Math.floor(number / 1000);
    }

    return result.trim();
}

function convertThreeDigitsToText(num) {
    const persianNumbers = ['صفر', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه'];
    const tens = ['ده', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'];
    const hundreds = ['صد', 'دویست', 'سیصد', 'چهارصد', 'پانصد', 'ششصد', 'هفتصد', 'هشتصد', 'نهصد'];

    let text = '';
    if (num >= 100) {
        text += hundreds[Math.floor(num / 100) - 1];
        num %= 100;
    }
    if (num >= 20) {
        text += ' ' + tens[Math.floor(num / 10) - 1];
        num %= 10;
    }
    if (num > 0) {
        text += ' ' + persianNumbers[num];
    }
    return text.trim();
}
