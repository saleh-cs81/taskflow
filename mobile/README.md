# TaskFlow Mobile (Flutter)

Employee mobile app for TaskFlow — tasks, live time tracking, bilingual EN/AR with
full RTL. Talks to the same REST API as the web client.

## Architecture

```
lib/
  core/
    config.dart            API base URL (override with --dart-define=API_BASE_URL=...)
    network/api_client.dart Dio client: JWT header + Accept-Language + 401 auto-refresh
    storage/token_store.dart Secure token storage (flutter_secure_storage)
    i18n/strings.dart       EN/AR strings
    providers.dart          Riverpod providers (api, locale, auth state)
  features/
    auth/                   login_screen + auth_repository
    tasks/                  tasks_screen + tasks_repository
    timer/                  timer_screen + timer_repository
    shell/                  home_shell (bottom nav + language toggle + logout)
  main.dart                 MaterialApp; locale drives RTL/LTR automatically
```

State: **Riverpod**. HTTP: **Dio** (with JWT refresh interceptor mirroring the web client).
Bilingual + RTL: `MaterialApp.locale` + `flutter_localizations` flip the entire UI for Arabic.

## Run

Requires the Flutter SDK (3.24+). This scaffold was authored without a local SDK,
so run these once you have Flutter installed:

```bash
cd mobile
# Generate the android/ios/etc. platform folders into this existing project:
flutter create .
flutter pub get
# Android emulator reaches your host API on 10.0.2.2:
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080/api/v1
```

Sign in with a TaskFlow account (e.g. the seeded `sara@acme.test`). The language
button in the app bar toggles English/Arabic and mirrors the layout.

## Status & next steps

Implemented: auth (login/logout, session restore, token refresh), my-tasks list,
live timer (start/stop), bilingual RTL/LTR.

Planned (per architecture doc): offline-first sync (Drift + outbox), push
notifications (FCM), an admin flavor (`main_admin.dart`), project/task detail,
comments, and file uploads.
