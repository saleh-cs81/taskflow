import 'package:flutter/widgets.dart';

// Lightweight bilingual strings (en/ar). Arabic flips the whole UI via
// Directionality (handled in main.dart by setting locale + textDirection).
class Strings {
  static const Map<String, Map<String, String>> _m = {
    'en': {
      'app': 'TaskFlow',
      'login': 'Sign in',
      'email': 'Email',
      'password': 'Password',
      'tasks': 'Tasks',
      'timer': 'Timer',
      'profile': 'Profile',
      'start': 'Start',
      'stop': 'Stop',
      'running': 'Running',
      'noTimer': 'No timer running',
      'logout': 'Log out',
      'language': 'العربية',
      'loginError': 'Invalid email or password.',
      'myTasks': 'My tasks',
    },
    'ar': {
      'app': 'تاسك فلو',
      'login': 'تسجيل الدخول',
      'email': 'البريد الإلكتروني',
      'password': 'كلمة المرور',
      'tasks': 'المهام',
      'timer': 'المؤقّت',
      'profile': 'الملف الشخصي',
      'start': 'ابدأ',
      'stop': 'إيقاف',
      'running': 'قيد التشغيل',
      'noTimer': 'لا يوجد مؤقّت قيد التشغيل',
      'logout': 'تسجيل الخروج',
      'language': 'English',
      'loginError': 'البريد الإلكتروني أو كلمة المرور غير صحيحة.',
      'myTasks': 'مهامي',
    },
  };

  final String lang;
  const Strings(this.lang);

  static Strings of(BuildContext context) =>
      Strings(Localizations.localeOf(context).languageCode == 'ar' ? 'ar' : 'en');

  String t(String key) => _m[lang]?[key] ?? _m['en']![key] ?? key;
}
