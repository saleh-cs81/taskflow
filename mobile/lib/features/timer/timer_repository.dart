import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/providers.dart';

class TimerRepository {
  TimerRepository(this._ref);
  final Ref _ref;

  Future<Map<String, dynamic>?> running() async {
    final api = _ref.read(apiClientProvider);
    final res = await api.dio.get('/time/running');
    return res.data as Map<String, dynamic>?;
  }

  Future<void> start(int projectId, {int? taskId}) async {
    final api = _ref.read(apiClientProvider);
    await api.dio.post('/time/start', data: {
      'projectId': projectId,
      'taskId': taskId,
      'note': null,
      'isBillable': true,
    });
  }

  Future<void> stop() async {
    final api = _ref.read(apiClientProvider);
    await api.dio.post('/time/stop');
  }
}

final timerRepositoryProvider = Provider((ref) => TimerRepository(ref));
