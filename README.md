# Sahand_Gear_Maker

**Sahand Gear Designer | طراح چرخ‌دنده سهند** — نرم‌افزار طراحی پارامتریک چرخ‌دنده برای ویندوز؛ فارسی، راست‌به‌چپ و کاملاً آفلاین.

[![core-build-test](https://github.com/pranses2011/Sahand_Gear_Maker/actions/workflows/core.yml/badge.svg)](https://github.com/pranses2011/Sahand_Gear_Maker/actions/workflows/core.yml)

## 🚀 شروع سریع

```bash
# ساخت هسته و اجرای آزمون‌ها (هر سیستم‌عاملی)
dotnet test tests/SahandGearMaker.Tests -c Release

# ساخت و انتشار برنامهٔ ویندوزی (روی ویندوز)
dotnet publish src/SahandGearMaker.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

پروژه‌های نمونه را از `samples/` باز کنید؛ فونت وزیرمتن همراه برنامه است و نیازی به نصب ندارد.

## 📁 ساختار مخزن

| مسیر | محتوا |
|---|---|
| `prompts/sahand-gear-designer/` | بستهٔ پرامپت محصول (شرح‌کار اصلی) |
| `src/` | هفت پروژه: Domain، Math، Geometry، Assembly، Persistence، Exports، App (WPF) |
| `tests/` | آزمون‌های xUnit — روی لینوکس/ویندوز اجرا می‌شوند |
| `samples/` | پروژه‌های نمونهٔ `.sgdproj` |
| `docs/` | معماری، ماتریس قابلیت، راهنماها و گزارش نهایی |

## 📚 مستندات

- [🏗️ معماری و تصمیم‌های فنی](docs/ARCHITECTURE.md)
- [📊 ماتریس قابلیت (واقعی/صادقانه)](docs/CAPABILITY-MATRIX.md)
- [🛠️ راهنمای ساخت و توسعه](docs/BUILD.md)
- [📘 راهنمای کاربر](docs/USER-GUIDE.md)
- [🔒 حریم خصوصی](docs/PRIVACY.md)
- [🏁 گزارش نهایی تحویل](docs/FINAL-REPORT.md)

## ✅ وضعیت کیفیت

هستهٔ هندسی، ماندگاری و خروجی‌ها با **۵۸ آزمون واحد مستقل** پوشش داده شده‌اند و در CI روی ubuntu اجرا می‌شوند. خروجی‌های مش (STL/OBJ/PLY/GLB) پس از نوشتن دوباره خوانده و اعتبارسنجی می‌شوند.

## 🧭 اصول محصول

- **صداقت قابلیت:** هیچ نوع/فرمت پیاده‌نشده به‌عنوان پشتیبانی‌شده نمایش داده نمی‌شود؛ مش مثلثی هرگز B-rep معرفی نمی‌شود.
- **آفلاین واقعی:** طراحی/ذخیره/خروجی بدون اینترنت؛ ارسال بازخورد فقط با رضایت و از HTTPS — و تا پیکربندی سرویس، صادقانه غیرفعال.
- **فارسی واقعی:** تمام متن‌ها از منبع بومی‌سازی؛ فونت وزیرمتن (OFL) همراه برنامه.
