import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/i18n/strings.dart';
import '../../core/providers.dart';
import '../auth/auth_repository.dart';
import '../tasks/tasks_screen.dart';
import '../timer/timer_screen.dart';

class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key});
  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final s = Strings.of(context);
    final pages = const [TasksScreen(), TimerScreen()];
    final titles = [s.t('myTasks'), s.t('timer')];

    return Scaffold(
      appBar: AppBar(
        title: Text(titles[_index]),
        actions: [
          TextButton(
            onPressed: () {
              final cur = ref.read(localeProvider).languageCode;
              ref.read(localeProvider.notifier).state = Locale(cur == 'ar' ? 'en' : 'ar');
            },
            child: Text(s.t('language'), style: const TextStyle(color: Colors.white)),
          ),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authRepositoryProvider).logout(),
          ),
        ],
      ),
      body: pages[_index],
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: [
          NavigationDestination(icon: const Icon(Icons.check_circle_outline), label: s.t('tasks')),
          NavigationDestination(icon: const Icon(Icons.timer_outlined), label: s.t('timer')),
        ],
      ),
    );
  }
}
