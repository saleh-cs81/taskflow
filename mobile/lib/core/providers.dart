import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'network/api_client.dart';
import 'storage/token_store.dart';

final tokenStoreProvider = Provider<TokenStore>((ref) => TokenStore());

final apiClientProvider = Provider<ApiClient>((ref) {
  final client = ApiClient(ref.read(tokenStoreProvider));
  client.locale = ref.watch(localeProvider).languageCode;
  return client;
});

// App locale (en/ar). Toggling rebuilds MaterialApp -> flips direction.
final localeProvider = StateProvider<Locale>((ref) => const Locale('en'));

// Whether a session token exists (drives the initial route).
final authStateProvider = StateProvider<bool>((ref) => false);
