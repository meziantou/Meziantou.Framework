// RxJS 7: observables, subjects, the operators (map, filter, scan, switchMap, mergeMap, concatMap, debounceTime,
// bufferCount, combineLatest, zip, retry, catchError, share...) and the schedulers (virtual time with the harness
// timers)
import { of, from, interval, timer, Subject, BehaviorSubject, ReplaySubject, combineLatest, zip, merge, concat, defer, throwError, EMPTY, firstValueFrom, lastValueFrom, asyncScheduler } from "rxjs";
import { map, filter, scan, reduce, take, takeUntil, switchMap, mergeMap, concatMap, exhaustMap, debounceTime, throttleTime, bufferCount, bufferTime, distinctUntilChanged, startWith, pairwise, retry, catchError, share, tap, delay, toArray, groupBy, finalize, timeout, withLatestFrom, auditTime } from "rxjs/operators";

export default async () => {
  const log = [];
  const record = name => ({ next: value => log.push(`${name}: ${JSON.stringify(value)}`), error: error => log.push(`${name} error: ${error.message}`), complete: () => log.push(`${name} complete`) });

  of(1, 2, 3, 4, 5).pipe(filter(value => value % 2 === 1), map(value => value * 10), scan((sum, value) => sum + value, 0)).subscribe(record("scan"));
  from([1, 1, 2, 2, 3, 1]).pipe(distinctUntilChanged(), pairwise(), toArray()).subscribe(record("pairwise"));
  from(["a", "bb", "c", "dd"]).pipe(groupBy(value => value.length), mergeMap(group => group.pipe(toArray(), map(values => [group.key, values])))).subscribe(record("groupBy"));
  zip(of(1, 2, 3), of("a", "b")).subscribe(record("zip"));
  concat(of(1), defer(() => of(2)), EMPTY, of(3)).pipe(reduce((acc, value) => acc.concat(value), [])).subscribe(record("concat"));

  let attempts = 0;
  defer(() => (++attempts < 3 ? throwError(() => new Error("fail " + attempts)) : of("ok after " + attempts))).pipe(retry(5)).subscribe(record("retry"));
  throwError(() => new Error("boom")).pipe(catchError(error => of("caught " + error.message))).subscribe(record("catchError"));

  const subject = new Subject();
  const behavior = new BehaviorSubject("initial");
  const replay = new ReplaySubject(2);
  combineLatest([subject, behavior]).subscribe(record("combineLatest"));
  subject.next(1);
  behavior.next("second");
  subject.next(2);
  [1, 2, 3].forEach(value => replay.next(value));
  replay.subscribe(record("replay"));

  const shared = interval(10).pipe(take(3), tap(value => log.push("tap " + value)), share());
  shared.subscribe(record("shared A"));
  shared.subscribe(record("shared B"));

  const clicks = new Subject();
  clicks.pipe(debounceTime(50)).subscribe(record("debounce"));
  clicks.pipe(throttleTime(30)).subscribe(record("throttle"));
  clicks.pipe(bufferCount(2)).subscribe(record("buffer"));
  clicks.pipe(switchMap(value => timer(25).pipe(map(() => "switched " + value)))).subscribe(record("switchMap"));
  clicks.pipe(exhaustMap(value => timer(40).pipe(map(() => "exhausted " + value)))).subscribe(record("exhaustMap"));
  clicks.pipe(concatMap(value => of(value).pipe(delay(5)))).subscribe(record("concatMap"));
  for (const [index, time] of [0, 10, 20, 100, 110].entries())
    setTimeout(() => clicks.next(index), time);
  setTimeout(() => clicks.complete(), 200);

  interval(7).pipe(takeUntil(timer(30)), finalize(() => log.push("finalized"))).subscribe(record("takeUntil"));
  timer(100).pipe(timeout(50), catchError(error => of(error.name))).subscribe(record("timeout"));
  merge(interval(15).pipe(take(2), map(value => "x" + value)), interval(20).pipe(take(2), map(value => "y" + value))).pipe(startWith("start")).subscribe(record("merge"));
  interval(10).pipe(take(5), withLatestFrom(interval(25)), auditTime(15)).subscribe(record("withLatestFrom"));
  interval(5).pipe(take(7), bufferTime(12)).subscribe(record("bufferTime"));

  log.push("first " + await firstValueFrom(of(9, 8)));
  log.push("last " + await lastValueFrom(timer(300, asyncScheduler).pipe(map(() => "late"))));
  return log.join("\n");
};
