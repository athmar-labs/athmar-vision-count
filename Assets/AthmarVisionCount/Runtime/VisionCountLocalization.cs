using System;

namespace AthmarLabs.VisionCount
{
    public static class VisionCountLocalization
    {
        public static bool IsArabic(string language)
        {
            return string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        }

        public static string Text(string key, string language)
        {
            var arabic = IsArabic(language);
            switch (key)
            {
                case "app_title": return arabic ? "عداد المخزون الذكي" : "Vision Inventory Count";
                case "pause": return arabic ? "إيقاف مؤقت" : "Pause";
                case "resume": return arabic ? "متابعة" : "Resume";
                case "review": return arabic ? "مراجعة" : "Review";
                case "confirm_export": return arabic ? "تأكيد وتصدير" : "Confirm & Export";
                case "cancel": return arabic ? "إلغاء" : "Cancel";
                case "delete_data": return arabic ? "حذف البيانات المحلية" : "Delete Local Data";
                case "language": return arabic ? "English" : "العربية";
                case "administration": return arabic ? "الإدارة" : "Admin";
                case "operator": return arabic ? "مرجع الموظف" : "Operator reference";
                case "location": return arabic ? "الموقع / الرف" : "Location / shelf";
                case "proposed": return arabic ? "المقترح" : "Proposed";
                case "confirmed": return arabic ? "المؤكد" : "Confirmed";
                case "empty_counts": return arabic ? "لم يتم اكتشاف أصناف بعد" : "No products detected yet";
                case "review_title": return arabic ? "مراجعة العد قبل الاعتماد" : "Review counts before confirmation";
                case "privacy_note": return arabic ? "لا يتم حفظ الصور. لا يتم التصدير قبل التأكيد." : "Images are not stored. Export requires confirmation.";
                case "exported": return arabic ? "تم حفظ وتصدير الجلسة" : "Session saved and exported";
                case "deleted": return arabic ? "تم حذف جميع البيانات المحلية" : "All local data was deleted";
                case "configuration_required": return arabic ? "يلزم إعداد نموذج الإنتاج وكتالوج الأصناف" : "Production model and SKU catalogue configuration required";
                case "requesting_camera_permission": return arabic ? "جارٍ طلب إذن الكاميرا" : "Requesting camera permission";
                case "scanning": return arabic ? "جارٍ المسح" : "Scanning";
                case "paused": return arabic ? "المسح متوقف مؤقتًا" : "Scanning paused";
                default: return key ?? string.Empty;
            }
        }
    }
}
