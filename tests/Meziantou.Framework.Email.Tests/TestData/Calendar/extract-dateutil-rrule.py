# Writes dateutil-rrule.tsv from tests/test_rrule.py of python-dateutil, without running it: it reads the calls
# assertEqual(list(rrule(...)), [datetime(...), ...]) whose frequency and parts the library supports.
# Usage: python3 -I extract-dateutil-rrule.py test_rrule.py > dateutil-rrule.tsv
import ast, sys
src = open(sys.argv[1], encoding='utf-8').read()
tree = ast.parse(src)
FREQ = {'YEARLY','MONTHLY','WEEKLY','DAILY'}
DAYS = {'MO','TU','WE','TH','FR','SA','SU'}
SUPPORTED = {'count','interval','until','bymonth','bymonthday','byweekday','bysetpos','wkst','dtstart'}
skipped = {}
out = []
def dt(node):
    if not (isinstance(node, ast.Call) and getattr(node.func,'id',None) in ('datetime','date')): raise ValueError('dt')
    a=[ast.literal_eval(x) for x in node.args]
    if node.keywords: raise ValueError('dtkw')
    while len(a)<6: a.append(0)
    return '%04d%02d%02dT%02d%02d%02d' % tuple(a[:6])
def day(node):
    if isinstance(node, ast.Name) and node.id in DAYS: return node.id
    if isinstance(node, ast.Call) and getattr(node.func,'id',None) in DAYS:
        return '%d%s' % (ast.literal_eval(node.args[0]), node.func.id)
    raise ValueError('day')
def nums(node):
    v=ast.literal_eval(node)
    return ','.join(str(x) for x in (v if isinstance(v,(tuple,list)) else [v]))
for fn in ast.walk(tree):
    if not isinstance(fn, ast.FunctionDef) or not fn.name.startswith('test'): continue
    for call in ast.walk(fn):
        if not (isinstance(call, ast.Call) and isinstance(call.func, ast.Attribute) and call.func.attr=='assertEqual' and len(call.args)==2): continue
        l, r = call.args
        if not (isinstance(l, ast.Call) and getattr(l.func,'id',None)=='list' and len(l.args)==1): continue
        rr = l.args[0]
        if not (isinstance(rr, ast.Call) and getattr(rr.func,'id',None)=='rrule' and rr.args and isinstance(rr.args[0], ast.Name)): continue
        freq = rr.args[0].id
        try:
            if freq not in FREQ: raise ValueError('freq '+freq)
            kw = {k.arg:k.value for k in rr.keywords}
            bad = set(kw)-SUPPORTED
            if bad: raise ValueError('kw '+','.join(sorted(bad)))
            if not isinstance(r, ast.List): raise ValueError('expected')
            expected = [dt(x) for x in r.elts]
            parts=['FREQ='+freq]
            if 'interval' in kw: parts.append('INTERVAL=%d'%ast.literal_eval(kw['interval']))
            if 'count' in kw: parts.append('COUNT=%d'%ast.literal_eval(kw['count']))
            if 'until' in kw: parts.append('UNTIL='+dt(kw['until']))
            if 'bymonth' in kw: parts.append('BYMONTH='+nums(kw['bymonth']))
            if 'bymonthday' in kw: parts.append('BYMONTHDAY='+nums(kw['bymonthday']))
            if 'byweekday' in kw:
                v=kw['byweekday']; els = v.elts if isinstance(v,(ast.Tuple,ast.List)) else [v]
                parts.append('BYDAY='+','.join(day(x) for x in els))
            if 'bysetpos' in kw: parts.append('BYSETPOS='+nums(kw['bysetpos']))
            if 'wkst' in kw: parts.append('WKST='+day(kw['wkst']))
            if 'dtstart' not in kw: raise ValueError('no dtstart')
            out.append((fn.name, dt(kw['dtstart']), ';'.join(parts), expected))
        except ValueError as e:
            skipped[str(e)] = skipped.get(str(e),0)+1
names=set()
for n,s,r,e in out:
    assert n not in names, n; names.add(n)
    # "-" when the rule has no occurrence, so that no line ends with white space
    print(f'{n}\t{s}\t{r}\t{",".join(e) or "-"}')
print(len(out), skipped, file=sys.stderr)
