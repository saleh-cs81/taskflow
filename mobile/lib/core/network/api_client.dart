import 'package:dio/dio.dart';
import '../config.dart';
import '../storage/token_store.dart';

// Dio wrapper: attaches the JWT + active language, and transparently refreshes
// the access token on a 401 (mirrors the web client's behaviour).
class ApiClient {
  ApiClient(this._tokens) {
    _dio = Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl))
      ..interceptors.add(InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _tokens.accessToken;
          if (token != null) options.headers['Authorization'] = 'Bearer $token';
          options.headers['Accept-Language'] = locale;
          handler.next(options);
        },
        onError: (e, handler) async {
          if (e.response?.statusCode == 401 && !_isRetry(e.requestOptions)) {
            if (await _refresh()) {
              final clone = await _retry(e.requestOptions);
              return handler.resolve(clone);
            }
            await _tokens.clear();
          }
          handler.next(e);
        },
      ));
  }

  late final Dio _dio;
  final TokenStore _tokens;
  String locale = 'en';

  Dio get dio => _dio;

  bool _isRetry(RequestOptions o) => o.extra['retried'] == true;

  Future<bool> _refresh() async {
    final refresh = await _tokens.refreshToken;
    if (refresh == null) return false;
    try {
      final res = await Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl))
          .post('/auth/refresh', data: {'refreshToken': refresh});
      await _tokens.save(res.data['accessToken'], res.data['refreshToken']);
      return true;
    } catch (_) {
      return false;
    }
  }

  Future<Response<dynamic>> _retry(RequestOptions o) async {
    final token = await _tokens.accessToken;
    return _dio.request(
      o.path,
      data: o.data,
      queryParameters: o.queryParameters,
      options: Options(
        method: o.method,
        headers: {...o.headers, 'Authorization': 'Bearer $token'},
        extra: {'retried': true},
      ),
    );
  }
}
