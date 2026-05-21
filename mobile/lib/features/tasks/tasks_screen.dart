import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/i18n/strings.dart';
import 'tasks_repository.dart';

final myTasksProvider = FutureProvider.autoDispose<List<TaskItem>>((ref) async {
  final repo = ref.read(tasksRepositoryProvider);
  final projects = await repo.projects();
  if (projects.isEmpty) return [];
  return repo.myTasks(projectId: projects.first['id'] as int);
});

class TasksScreen extends ConsumerWidget {
  const TasksScreen({super.key});

  static const _priorityColors = [Colors.grey, Colors.blue, Colors.orange, Colors.red];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = Strings.of(context);
    final async = ref.watch(myTasksProvider);
    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => Center(child: Text('$e')),
      data: (tasks) => RefreshIndicator(
        onRefresh: () async => ref.invalidate(myTasksProvider),
        child: ListView.separated(
          itemCount: tasks.length,
          separatorBuilder: (_, __) => const Divider(height: 1),
          itemBuilder: (_, i) {
            final t = tasks[i];
            return ListTile(
              leading: CircleAvatar(
                radius: 6,
                backgroundColor: _priorityColors[t.priority.clamp(0, 3)],
              ),
              title: Text(t.title),
              trailing: t.status == 3 ? const Icon(Icons.check, color: Colors.green) : null,
            );
          },
        ),
      ),
    );
  }
}
