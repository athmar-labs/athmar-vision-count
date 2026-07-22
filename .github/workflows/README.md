# GitHub Actions Workflows - دليل شامل / Complete Guide

This directory contains all CI/CD workflows for the LHD project (Unity + Node.js/React).

يحتوي هذا المجلد على جميع workflows الخاصة بـ CI/CD للمشروع (Unity + Node.js/React).

---

## 📋 Available Workflows / الـ Workflows المتوفرة

### 1. **Node.js CI/CD** (`ci-nodejs.yml`)
Build and test Node.js/TypeScript application (Backend + Frontend)

بناء واختبار تطبيق Node.js/TypeScript (Backend + Frontend)

- **Triggers / متى يعمل**: 
  - Push/PR to `main` affecting Node.js files
  - Manual (`workflow_dispatch`)
- **Features / المميزات**:
  - ✅ TypeScript type checking
  - 🏗️ Full project build
  - 📦 Upload build artifacts
  - ⚡ npm caching for speed
  
**Required Secrets / Secrets المطلوبة**: None / لا يوجد

### 2. **Build Web Frontend** (`build-web.yml`)
Build React Frontend with Vite

بناء React Frontend باستخدام Vite

- **Triggers / متى يعمل**: 
  - Push/PR to `main` affecting frontend files
  - Manual (`workflow_dispatch`)
- **Features / المميزات**:
  - 📦 Production build with Vite
  - ⚡ node_modules caching
  - ✅ Build verification
  - 📤 Upload artifacts (14 days retention)

**Required Secrets / Secrets المطلوبة**: None / لا يوجد

### 3. **Build Android APK** (`build-android.yml`)
Build Unity project for Android

بناء مشروع Unity لنظام Android

**Triggers / متى يعمل:**
- Push to `main` branch (when files in `Assets/`, `Packages/`, or `ProjectSettings/` change)
- Pull requests to `main` branch
- Manual (`workflow_dispatch`)

**Features / المميزات:**
- 🎮 Build Android APK
- 💾 Unity Library caching
- 📱 Keystore signing support
- 📦 Upload APK artifact (14 days retention)
- ⏱️ Build time: First ~20-40 min, Cached ~10-15 min

**Required Secrets / Secrets المطلوبة**:
- `UNITY_LICENSE` ✅ (Required / إلزامي)
- `UNITY_EMAIL` ✅ (Required / إلزامي)
- `UNITY_PASSWORD` ✅ (Required / إلزامي)
- `ANDROID_KEYSTORE_BASE64` (Optional / اختياري)
- `ANDROID_KEYSTORE_PASS` (Optional / اختياري)
- `ANDROID_KEYALIAS_NAME` (Optional / اختياري)
- `ANDROID_KEYALIAS_PASS` (Optional / اختياري)

### 4. **Build Unity Multi-Platform** (`build-unity-multiplatform.yml`)
Build Unity for multiple platforms (Windows, Linux, WebGL)

بناء Unity لمنصات متعددة (Windows, Linux, WebGL)

**Triggers / متى يعمل:** 
- Manual only (`workflow_dispatch`)

**Features / المميزات**:
- 🖥️ Build for: Windows, Linux, macOS, WebGL
- ⚙️ Choose single platform or all
- 💾 Platform-specific caching
- 📦 Separate artifacts per platform

**Required Secrets / Secrets المطلوبة**:
- `UNITY_LICENSE` ✅ (Required / إلزامي)
- `UNITY_EMAIL` ✅ (Required / إلزامي)
- `UNITY_PASSWORD` ✅ (Required / إلزامي)

### 5. **Build via RunPod** (`build-runpod.yml`)
Build the Node.js/React application using RunPod as a self-hosted GitHub Actions runner

بناء تطبيق Node.js/React باستخدام RunPod كـ self-hosted runner لـ GitHub Actions

**Triggers / متى يعمل:**
- Manual only (`workflow_dispatch`)

**Features / المميزات:**
- 🚀 Starts a RunPod pod as a GitHub Actions self-hosted runner
- 🏗️ Full project build (Frontend + Backend) on RunPod infrastructure
- 📦 Upload build artifacts (7 days retention)
- 🛑 Automatically terminates the RunPod pod after build

**Build time / وقت البناء:** ~5–15 min (including runner startup)

**Required Secrets / Secrets المطلوبة**:
- `RUNPOD_API_KEY` ✅ (Required / إلزامي) — RunPod API key from [runpod.io/console/user/settings](https://www.runpod.io/console/user/settings)
- `GH_PAT` ✅ (Required / إلزامي) — GitHub Personal Access Token with `repo` scope

---

### 6. **Build Unity Android APK via RunPod** (`build-android-runpod.yml`)
Build the Unity Android APK using RunPod as a self-hosted runner — ideal when local disk space is insufficient for Unity

بناء Unity Android APK باستخدام RunPod كـ self-hosted runner — مثالي عندما لا تكفي مساحة القرص المحلية لـ Unity

**Triggers / متى يعمل:**
- Manual only (`workflow_dispatch`)

**Features / المميزات:**
- 🚀 Starts a RunPod pod with **60 GB disk** + 16 GB RAM (Unity needs ~40–60 GB)
- 🎮 Builds Unity Android APK via `game-ci/unity-builder@v4` (same as `build-android.yml`)
- 💾 Caches Unity Library folder to speed up future runs
- 📦 Upload APK artifact (14 days retention)
- 🛑 Always terminates the RunPod pod when done (even on failure) to avoid extra charges
- ⏱️ Build time: ~30–60 min (first run pulls ~20 GB Unity Docker image)

**Required Secrets / Secrets المطلوبة**:
- `RUNPOD_API_KEY` ✅ (Required / إلزامي) — RunPod API key from [runpod.io/console/user/settings](https://www.runpod.io/console/user/settings)
- `GH_PAT` ✅ (Required / إلزامي) — GitHub Personal Access Token with `repo` scope
- `UNITY_LICENSE` ✅ (Required / إلزامي)
- `UNITY_EMAIL` ✅ (Required / إلزامي)
- `UNITY_PASSWORD` ✅ (Required / إلزامي)
- `ANDROID_KEYSTORE_BASE64` (Optional / اختياري)
- `ANDROID_KEYSTORE_PASS` (Optional / اختياري)
- `ANDROID_KEYALIAS_NAME` (Optional / اختياري)
- `ANDROID_KEYALIAS_PASS` (Optional / اختياري)

---

### 7. **Acquire Unity License** (`activation.yml`)
Unity license activation instructions

تعليمات الحصول على ترخيص Unity

**Triggers / متى يعمل:** 
- Manual only (`workflow_dispatch`)

**⚠️ Important / مهم:**
This workflow now directs you to the updated activation documentation.

هذا الـ workflow الآن يوجهك إلى توثيق التفعيل المحدث.

**Steps to get Unity license / خطوات الحصول على ترخيص Unity:**
1. Visit the official guide / قم بزيارة الدليل الرسمي: https://game.ci/docs/github/activation
2. Follow the updated instructions / اتبع التعليمات المحدثة
3. Add the license as `UNITY_LICENSE` secret / أضف الترخيص كـ Secret باسم UNITY_LICENSE

**Required once / مطلوب مرة واحدة:** Only initially or when license expires / فقط في البداية أو عند انتهاء الترخيص

---

## 🔐 Required Secrets / Secrets المطلوبة

### RunPod Secrets (Required for RunPod builds / إلزامية لبناء RunPod):

```
RUNPOD_API_KEY=your-runpod-api-key
GH_PAT=your-github-personal-access-token
```

### Unity Secrets (Required for Unity builds / إلزامية لبناء Unity):

```
UNITY_EMAIL=your-unity-email@example.com
UNITY_PASSWORD=your-unity-password
UNITY_LICENSE=<contents of .ulf file>
```

### Android Signing Secrets (Optional for signed APKs / اختيارية للتوقيع):

```
ANDROID_KEYSTORE_BASE64=<keystore file as base64>
ANDROID_KEYSTORE_PASS=your-keystore-password
ANDROID_KEYALIAS_NAME=your-key-alias
ANDROID_KEYALIAS_PASS=your-key-password
```

### How to add Secrets / كيفية إضافة Secrets:
1. Go to **Settings > Secrets and variables > Actions**
2. Click "New repository secret"
3. Add name and value / أضف الاسم والقيمة
4. Save / احفظ

---

## 🚀 How to Use / كيفية الاستخدام

### Automatic Builds / البناء التلقائي:
Workflows run automatically on push/PR to relevant files

تعمل workflows تلقائياً عند push/PR للملفات المتعلقة

### Manual Builds / البناء اليدوي:
1. Go to **Actions** tab / اذهب إلى تبويب Actions
2. Select desired workflow / اختر الـ workflow المطلوب
3. Click "Run workflow" / اضغط على Run workflow
4. Choose options (if any) / اختر الخيارات
5. Click "Run workflow" / اضغط على Run workflow

---

## 📦 Download Artifacts / تحميل الـ Artifacts

After build completes / بعد انتهاء البناء:
1. Go to **Actions** tab
2. Select completed workflow run
3. Scroll to **Artifacts** section
4. Click to download

**Retention periods / مدة الاحتفاظ**:
- Node.js builds: 7 days / أيام
- Web builds: 14 days / يوم
- Unity builds: 14 days / يوم

---

## 🔍 Troubleshooting / استكشاف الأخطاء

### Unity build fails / بناء Unity يفشل
**Solution / الحل**:
- Verify `UNITY_LICENSE` secret exists and is complete / تحقق من وجود Secret واكتماله
- Re-run activation workflow if expired / أعد تشغيل activation إذا انتهى
- Check logs for details / راجع الـ logs للتفاصيل

### Node.js build fails / بناء Node.js يفشل
**Solution / الحل**:
- Verify `package.json` is correct / تحقق من صحة package.json
- Try `npm ci` locally / جرب npm ci محلياً
- Check dependencies / تحقق من الـ dependencies

### TypeScript errors / أخطاء TypeScript
**Solution / الحل**:
- Run `npm run check` locally / جرب npm run check محلياً
- Fix TypeScript errors / أصلح الأخطاء
- Commit changes / commit التغييرات

### Build runs out of disk space / نفاذ مساحة القرص
**Solution / الحل**:
- Workflow includes automatic cleanup / يتضمن workflow تنظيف تلقائي
- Large projects may need self-hosted runner / المشاريع الكبيرة قد تحتاج self-hosted runner

### APK won't install / APK لا يثبت
**Solution / الحل**:
- Enable "Install from Unknown Sources" / فعّل "التثبيت من مصادر غير معروفة"
- For Play Store, provide keystore secrets / لـ Play Store، وفر keystore secrets

---

## 🎯 Best Practices / أفضل الممارسات

1. **Use Caching / استخدم Caching**: All workflows use caching for speed / جميع الـ workflows تستخدم caching
2. **Secure Secrets / أمان Secrets**: Never expose secrets in code / لا تكشف secrets في الكود
3. **PR Checks / فحوصات PR**: Workflows run on PRs / تعمل على Pull Requests
4. **Artifacts / الـ Artifacts**: Auto-deleted after retention period / تحذف تلقائياً بعد المدة المحددة
5. **Timeouts / المهل الزمنية**: All jobs have timeouts / كل job له timeout

---

## 📊 Project Structure / هيكل المشروع

```
LHD/
├── Assets/          # Unity game assets
├── client/          # React frontend (Vite)
├── server/          # Express backend
├── shared/          # Shared TypeScript code
├── .github/
│   └── workflows/   # CI/CD workflows (هنا)
└── package.json     # Node.js dependencies
```

**Technologies / التقنيات**:
- Unity: **2022.3.22f1**
- Node.js: **20.x LTS**
- React: **18.x**
- TypeScript: **5.x**
- Vite: **5.x**
- Package Manager: **npm**

---

## 📚 Additional Resources / موارد إضافية

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [RunPod Documentation](https://docs.runpod.io/)
- [Unity GameCI Documentation](https://game.ci/)
- [Vite Build Guide](https://vitejs.dev/guide/build.html)
- [Node.js Best Practices](https://github.com/goldbergyoni/nodebestpractices)

**Project Documentation / توثيق المشروع**:
- `/Docs/CI_CD_SETUP.md` - Complete CI/CD setup guide
- `/replit.md` - Project overview and architecture
- `/Docs/RUNBOOK.md` - Setup and troubleshooting guide

---

## 🤝 Contributing / المساهمة

To improve or add new workflows / لتحسين أو إضافة workflows جديدة:
1. Create a new branch / أنشئ فرع جديد
2. Add/modify workflow / أضف/عدل الـ workflow
3. Test changes / اختبر التغييرات
4. Create Pull Request / أنشئ Pull Request

---

**Last Updated / آخر تحديث**: February 2026 / فبراير ٢٠٢٦

**Maintained by / يشرف عليه**: LHD Team
