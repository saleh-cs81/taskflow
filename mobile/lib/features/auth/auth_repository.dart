import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/providers.dart';

class AuthRepository {
  AuthRepository(this._ref);
  final Ref _ref;

  Future<void> login(String email, String password) async {
    final api = _ref.read(apiClientProvider);
    final res = await api.dio.post('/auth/login', data: {
      'email': email,
      'password': password,
    });
    await _ref
        .read(tokenStoreProvider)
        .save(res.data['accessToken'], res.data['refreshToken']);
    _ref.read(authStateProvider.notifier).state = true;
  }

  Future<void> logout() async {
    await _ref.read(tokenStoreProvider).clear();
    _ref.read(authStateProvider.notifier).state = false;
  }

  Future<bool> hasSession() async {
    final token = await _ref.read(tokenStoreProvider).accessToken;
    final ok = token != null;
    _ref.read(authStateProvider.notifier).state = ok;
    return ok;
  }
}

final authRepositoryProvider = Provider((ref) => AuthRepository(ref));
