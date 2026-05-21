import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/i18n/strings.dart';
import '../tasks/tasks_repository.dart';
import 'timer_repository.dart';

final runningTimerProvider = FutureProvider.autoDispose((ref) async {
  return ref.read(timerRepositoryProvider).running();
});

class TimerScreen extends ConsumerWidget {
  const TimerScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = Strings.of(context);
    final async = ref.watch(runningTimerProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => Center(child: Text('$e')),
      data: (running) {
        final isRunning = running != null;
        return Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(isRunning ? Icons.timer : Icons.timer_off,
                  size: 64, color: isRunning ? Colors.green : Colors.grey),
              const SizedBox(height: 12),
              Text(isRunning ? s.t('running') : s.t('noTimer'),
                  style: Theme.of(context).textTheme.titleMedium),
              const SizedBox(height: 24),
              FilledButton.icon(
                icon: Icon(isRunning ? Icons.stop : Icons.play_arrow),
                label: Text(isRunning ? s.t('stop') : s.t('start')),
                style: FilledButton.styleFrom(
                    backgroundColor: isRunning ? Colors.red : Colors.green),
                onPressed: () async {
                  final repo = ref.read(timerRepositoryProvider);
                  if (isRunning) {
                    await repo.stop();
                  } else {
                    final projects = await ref.read(tasksRepositoryProvider).projects();
                    if (projects.isNotEmpty) {
                      await repo.start(projects.first['id'] as int);
                    }
                  }
                  ref.invalidate(runningTimerProvider);
                },
              ),
            ],
          ),
        );
      },
    );
  }
}
