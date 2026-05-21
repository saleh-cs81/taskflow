import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/i18n/strings.dart';
import '../../core/providers.dart';
import 'auth_repository.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _email = TextEditingController(text: 'sara@acme.test');
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  Future<void> _submit() async {
    setState(() { _busy = true; _error = null; });
    try {
      await ref.read(authRepositoryProvider).login(_email.text.trim(), _password.text);
    } catch (_) {
      setState(() => _error = Strings.of(context).t('loginError'));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(s.t('app')),
        actions: [
          TextButton(
            onPressed: () {
              final cur = ref.read(localeProvider).languageCode;
              ref.read(localeProvider.notifier).state = Locale(cur == 'ar' ? 'en' : 'ar');
            },
            child: Text(s.t('language'), style: const TextStyle(color: Colors.white)),
          ),
        ],
      ),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(_error!, style: const TextStyle(color: Colors.red)),
              ),
            TextField(controller: _email, decoration: InputDecoration(labelText: s.t('email'))),
            const SizedBox(height: 12),
            TextField(controller: _password, obscureText: true, decoration: InputDecoration(labelText: s.t('password'))),
            const SizedBox(height: 20),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: _busy ? null : _submit,
                child: _busy ? const CircularProgressIndicator() : Text(s.t('login')),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
