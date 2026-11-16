import { Temporal } from 'temporal-polyfill';

export function calendarDay(zone: string, instant = new Date()) {
  return Temporal.Instant.from(instant.toISOString()).toZonedDateTimeISO(zone).toPlainDate();
}

export function calendarPeriod(anchor: string, zone: string, weekly: boolean) {
  let start = Temporal.PlainDate.from(anchor);
  if (weekly) start = start.subtract({ days: start.dayOfWeek - 1 });
  const length = weekly ? 7 : 1;
  const days = Array.from({ length }, (_, index) => {
    const date = start.add({ days: index });
    return {
      date,
      from: date.toZonedDateTime(zone).toInstant().toString(),
      to: date.add({ days: 1 }).toZonedDateTime(zone).toInstant().toString(),
    };
  });
  return { days, from: days[0]!.from, to: days.at(-1)!.to };
}

export function displayScheduleTime(value: string | null, zone: string) {
  return value
    ? new Intl.DateTimeFormat('en', {
        timeZone: zone,
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(new Date(value))
    : 'Not scheduled';
}

export function scheduleLocal(value: string | null, zone: string) {
  return value
    ? Temporal.Instant.from(value)
        .toZonedDateTimeISO(zone)
        .toPlainDateTime()
        .toString({ smallestUnit: 'minute' })
    : '';
}

export function scheduleInstant(value: string, zone: string, previous: string | null) {
  if (previous && scheduleLocal(previous, zone) === value) return previous;
  try {
    return Temporal.PlainDateTime.from(value)
      .toZonedDateTime(zone, { disambiguation: 'reject' })
      .toInstant()
      .toString();
  } catch {
    throw new Error(
      `Choose a time that occurs exactly once in ${zone}. This time falls in a daylight-saving gap or overlap.`,
    );
  }
}

export function moveSchedule(start: string, end: string, day: Temporal.PlainDate, zone: string) {
  const startAt = Temporal.Instant.from(start).toZonedDateTimeISO(zone);
  const endAt = Temporal.Instant.from(end).toZonedDateTimeISO(zone);
  const days = startAt.toPlainDate().until(endAt.toPlainDate()).days;
  return {
    start: day.toPlainDateTime(startAt.toPlainTime()).toString({ smallestUnit: 'minute' }),
    end: day
      .add({ days })
      .toPlainDateTime(endAt.toPlainTime())
      .toString({ smallestUnit: 'minute' }),
  };
}
