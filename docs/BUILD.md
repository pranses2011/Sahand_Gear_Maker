# 🛠️ راهنمای ساخت و توسعه

## پیش‌نیازها

| ابزار | نسخه | برای |
|---|---|---|
| .NET SDK | 8.0 (LTS) | ساخت همهٔ بخش‌ها |
| Windows 10/11 | x64 | ساخت/اجرای پروژهٔ WPF (`SahandGearMaker.App`) |
| (اختیاری) Git | هر | دریافت مخزن |

> **نکته:** هستهٔ برنامه (Domain/Math/Geometry/Assembly/Persistence/Exports و آزمون‌ها) روی لینوکس و macOS هم ساخته و آزمون می‌شود؛ فقط پروژهٔ UI به ویندوز نیاز دارد.

## ساخت هسته و اجرای آزمون‌ها (هر سیستمی)

```bash
# ساخت همهٔ کتابخانه‌های هسته
dotnet build src/SahandGearMaker.Geometry -c Release

# اجرای آزمون‌ها (۵۸ آزمون)
dotnet test tests/SahandGearMaker.Tests -c Release
```

## ساخت برنامهٔ ویندوزی

```bash
# ساخت (روی ویندوز)
dotnet build src/SahandGearMaker.App -c Release

# انتشار به‌صورت self-contained — بدون نیاز به نصب runtime/فونت/اینترنت
dotnet publish src/SahandGearMaker.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -o publish/win-x64
```

خروجی `publish/win-x64` یک بستهٔ portable کامل است؛ `SahandGearDesigner.exe` را مستقیم اجرا کنید.

> فونت وزیرمتن (مجوز OFL 1.1) به‌صورت منبع داخل برنامه بسته‌بندی شده و **نصب فونت در ویندوز لازم نیست**. متن مجوز در `src/SahandGearMaker.App/Assets/Fonts/LICENSE-OFL.txt` است.

## ساختار مخزن

```text
src/
  SahandGearMaker.Domain/     → مدل داده، پارامترها، کاتالوگ قابلیت، اعتبارسنجی
  SahandGearMaker.Math/       → جبر خطی سبک (Vector3، Triangle)
  SahandGearMaker.Geometry/   → موتور اینولوت، سازنده‌های مش، اعتبارسنجی توپولوژی
  SahandGearMaker.Assembly/   → جفت‌ها، قطار مرکب، سیاره‌ای (ویلیس)
  SahandGearMaker.Persistence/→ فایل بومی .sgdproj (ذخیرهٔ اتمی، مهاجرت)
  SahandGearMaker.Exports/    → STL/OBJ/PLY/GLB + اعتبارسنجی پس از نوشتن
  SahandGearMaker.App/        → پوستهٔ WPF/MVVM فارسی RTL (net8.0-windows)
tests/
  SahandGearMaker.Tests/      → xUnit (روی هر OS اجرا می‌شود)
samples/                      → پروژه‌های نمونهٔ .sgdproj
docs/                         → معماری، ماتریس قابلیت، راهنماها
prompts/                      → بستهٔ پرامپت محصول (ورودی کار)
.github/workflows/core.yml    → CI: build+test هسته روی ubuntu
```

## قواعد معماری

- جهت وابستگی یک‌طرفه: `App → همه`، `Exports/Assembly/Persistence → Domain(+Geometry)`، `Geometry → Domain(+Math)`، `Domain → هیچ`.
- هیچ لایه‌ای به WPF ارجاع ندارد؛ به همین دلیل هسته روی لینوکس آزمون می‌شود.
- افزودن خانوادهٔ جدید چرخ‌دنده: پیاده‌سازی `IProfileGenerator` + سازندهٔ مش + ردیف در `CapabilityCatalog` + آزمون‌های مستقل.
- افزودن فرمت خروجی: متد صادرات در `MeshExporter` + اعتبارسنجی پس از نوشتن + ردیف ماتریس + آزمون.

## CI

هر push/PR روی `main`، گردش‌کار `core-build-test` را اجرا می‌کند: ساخت شش کتابخانهٔ هسته + اجرای کامل آزمون‌ها روی ubuntu-latest.

## عیب‌یابی

| مشکل | راه‌حل |
|---|---|
| `NETSDK1100` روی لینوکس هنگام ساخت کل راهکار | فقط پروژهٔ `App` ویندوزی است: `dotnet build src/SahandGearMaker.Geometry` و `dotnet test tests/...` بسازید |
| فونت فارسی نمایش داده نمی‌شود | فایل‌های `Assets/Fonts/*.ttf` باید به‌صورت `Resource` در پروژه باشند (پیش‌فرض همین است) |
| آزمونی شکست خورد | با `dotnet test tests/SahandGearMaker.Tests --logger "console;verbosity=detailed"` جزئیات را ببینید |
