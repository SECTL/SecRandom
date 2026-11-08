import json
import sys
from collections import defaultdict

# Attribution helper for a dotnet-trace Speedscope export of a sampled-thread-time profile.
# Usage: python Drill-Speedscope.py <speedscope.json> <frame substring> [topN]
# It counts SAMPLES (one per marker frame open), never timestamp differences, and reports
# the nearest enclosing frame (the caller) and the nearest enclosed frames (the callees),
# so a leaf like Monitor.Enter_Slowpath can be traced back to the code that takes the lock.

path = sys.argv[1]
needle = sys.argv[2]
top = int(sys.argv[3]) if len(sys.argv) > 3 else 15

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


WRAPPERS = ('(Non-Activities)', 'Threads', '?!?', 'Process64', 'Thread (',
            'CPU_TIME', 'UNMANAGED_CODE_TIME')


def is_wrapper(n):
    return any(n.startswith(m) for m in WRAPPERS)


best = None
for prof in data['profiles']:
    if prof.get('type') != 'evented' or not prof['events']:
        continue
    if best is None or len(prof['events']) > len(best['events']):
        best = prof

stack = []
total = 0
hits = 0
parents = defaultdict(int)
children = defaultdict(int)
full_examples = []

for ev in best['events']:
    if ev['type'] == 'O':
        stack.append(ev['frame'])
        if fname(stack[-1]).startswith(('CPU_TIME', 'UNMANAGED_CODE_TIME')):
            total += 1
            chain = [fname(i) for i in stack if not is_wrapper(fname(i))]
            idx = [i for i, n in enumerate(chain) if needle in n]
            if idx:
                hits += 1
                first, last = idx[0], idx[-1]
                parents[chain[first - 1] if first > 0 else '<root>'] += 1
                children[chain[last + 1] if last + 1 < len(chain) else '<leaf>'] += 1
                if len(full_examples) < 3:
                    full_examples.append(chain)
    else:
        if stack:
            stack.pop()

print(f'file={path} needle={needle!r}')
print(f'samples={total}  samples containing it={hits} ({100.0*hits/max(total,1):.2f}%)')
print()
print('=== CALLER (nearest enclosing frame, self) ===')
for name, n in sorted(parents.items(), key=lambda kv: -kv[1])[:top]:
    print(f'{100.0*n/max(hits,1):6.2f}%  {n:5d}  {name}')
print()
print('=== CALLEE (nearest enclosed frame) ===')
for name, n in sorted(children.items(), key=lambda kv: -kv[1])[:top]:
    print(f'{100.0*n/max(hits,1):6.2f}%  {n:5d}  {name}')
print()
for i, chain in enumerate(full_examples):
    print(f'--- example {i+1} (root -> leaf)')
    for step in chain[:18]:
        print('     ' + step)
    print()
