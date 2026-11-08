import json
import sys
from collections import defaultdict

# dotnet-trace Speedscope output for Windows profiles emits one evented profile per thread.
# Each sample is a marker frame (CPU_TIME / UNMANAGED_CODE_TIME) opened on top of the managed
# stack. Only a CPU_TIME marker means the thread was actually running managed code; an
# UNMANAGED_CODE_TIME marker means it sat in native code, which for this app is usually a
# blocking wait (Monitor.Enter, IOCP, Sleep). So presence-in-stack alone overstates cost:
# always filter by marker kind before calling something a CPU hotspot.

path = sys.argv[1]
top = int(sys.argv[2]) if len(sys.argv) > 2 else 30
with open(path, 'r', encoding='utf-8') as f:
    data = json.load(f)

frames = data['shared']['frames']


def fname(i):
    fr = frames[i]
    name = fr.get('name') or '?'
    file_ = fr.get('file') or ''
    if file_:
        name = name + ' @ ' + file_.replace('\\', '/').split('/')[-1]
    return name


WRAPPERS = ('(Non-Activities)', 'Threads', '?!?', 'Process64', 'Thread (')


def is_wrapper(n):
    return any(n.startswith(m) for m in WRAPPERS)


best = None
for prof in data['profiles']:
    if prof.get('type') != 'evented' or not prof['events']:
        continue
    if best is None or len(prof['events']) > len(best['events']):
        best = prof

marker_count = defaultdict(int)
cpu_leaf = defaultdict(int)
cpu_presence = defaultdict(int)
unm_leaf = defaultdict(int)
total = 0
cpu_total = 0
examples = {}

stack = []
for ev in best['events']:
    if ev['type'] == 'O':
        stack.append(ev['frame'])
        nm = fname(stack[-1])
        if nm.startswith('CPU_TIME') or nm.startswith('UNMANAGED_CODE_TIME'):
            total += 1
            marker_count[nm.split(' ')[0]] += 1
            chain = [fname(i) for i in stack[:-1] if not is_wrapper(fname(i))]
            if not chain:
                continue
            if nm.startswith('CPU_TIME'):
                cpu_total += 1
                for f in dict.fromkeys(chain):
                    cpu_presence[f] += 1
                cpu_leaf[chain[-1]] += 1
                examples.setdefault(chain[-1], chain)
            else:
                unm_leaf[chain[-1]] += 1
    else:
        if stack:
            stack.pop()

print(f'file={path}  UI thread={best.get("name")}')
print(f'samples total={total}  cpu={cpu_total} ({100.0*cpu_total/max(total,1):.1f}%)  '
      f'unmanaged={total-cpu_total} ({100.0*(total-cpu_total)/max(total,1):.1f}%)')
print('markers: ' + ', '.join(f'{k}={v}' for k, v in marker_count.items()))
print()

print('=== CPU SAMPLES BY FRAME PRESENCE (share of CPU samples) ===')
for name, n in sorted(cpu_presence.items(), key=lambda kv: -kv[1])[:top]:
    print(f'{100.0*n/max(cpu_total,1):6.2f}%  {n:5d}  {name}')

print()
print('=== CPU LEAF (where running managed code actually was) ===')
for name, n in sorted(cpu_leaf.items(), key=lambda kv: -kv[1])[:top]:
    print(f'{100.0*n/max(cpu_total,1):6.2f}%  {n:5d}  {name}')

print()
print('=== TOP BLOCKED/UNMANAGED LEAVES (not CPU) ===')
for name, n in sorted(unm_leaf.items(), key=lambda kv: -kv[1])[:12]:
    print(f'{n:5d}  {name}')

print()
print('=== CALL PATH for top CPU leaves ===')
for name, n in sorted(cpu_leaf.items(), key=lambda kv: -kv[1])[:5]:
    print(f'--- {100.0*n/max(cpu_total,1):.1f}% ({n})')
    for step in reversed(examples[name][-12:]):
        print('     ' + step)
