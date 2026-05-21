class AppConfig {
  // Point this at your TaskFlow API. For Android emulator use 10.0.2.2.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5080/api/v1',
  );
}
