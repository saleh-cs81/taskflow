import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/providers.dart';

class TaskItem {
  TaskItem({required this.id, required this.title, required this.status, required this.priority});
  final int id;
  final String title;
  final int status;
  final int priority;

  factory TaskItem.fromJson(Map<String, dynamic> j) => TaskItem(
        id: j['id'],
        title: j['title'] ?? '',
        status: j['status'] ?? 0,
        priority: j['priority'] ?? 1,
      );
}

class TasksRepository {
  TasksRepository(this._ref);
  final Ref _ref;

  Future<List<TaskItem>> myTasks({int? projectId}) async {
    final api = _ref.read(apiClientProvider);
    // Lists tasks for a project; pass projectId from a project picker in a full app.
    final res = await api.dio.get('/tasks', queryParameters: {
      if (projectId != null) 'projectId': projectId,
      'pageSize': 100,
    });
    final items = (res.data['items'] as List).cast<Map<String, dynamic>>();
    return items.map(TaskItem.fromJson).toList();
  }

  Future<List<Map<String, dynamic>>> projects() async {
    final api = _ref.read(apiClientProvider);
    final res = await api.dio.get('/projects', queryParameters: {'pageSize': 100});
    return (res.data['items'] as List).cast<Map<String, dynamic>>();
  }
}

final tasksRepositoryProvider = Provider((ref) => TasksRepository(ref));
