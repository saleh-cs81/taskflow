import 'package:flutter_secure_storage/flutter_secure_storage.dart';

// Securely persists JWT access + refresh tokens on the device.
class TokenStore {
  static const _storage = FlutterSecureStorage();
  static const _kAccess = 'tf_access';
  static const _kRefresh = 'tf_refresh';

  Future<void> save(String access, String refresh) async {
    await _storage.write(key: _kAccess, value: access);
    await _storage.write(key: _kRefresh, value: refresh);
  }

  Future<String?> get accessToken => _storage.read(key: _kAccess);
  Future<String?> get refreshToken => _storage.read(key: _kRefresh);

  Future<void> clear() async {
    await _storage.delete(key: _kAccess);
    await _storage.delete(key: _kRefresh);
  }
}
