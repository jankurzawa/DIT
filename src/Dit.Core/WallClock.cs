namespace Dit.Core;

public static class WallClock
{
    public static DateTimeOffset Resolve(DateTime date,int hour,int minute,int second,TimeZoneInfo zone,TimeSpan? ambiguousOffset=null)
    {
        var local=DateTime.SpecifyKind(date.Date.AddHours(hour).AddMinutes(minute).AddSeconds(second),DateTimeKind.Unspecified);
        if(hour is <0 or >23||minute is <0 or >59||second is <0 or >59)throw new ArgumentException("Niepoprawna godzina.");
        if(zone.IsInvalidTime(local))throw new ArgumentException("Ta godzina nie istnieje w wybranej strefie podczas zmiany czasu. Wybierz inną godzinę.");
        if(zone.IsAmbiguousTime(local))
        {
            var offsets=zone.GetAmbiguousTimeOffsets(local);
            if(ambiguousOffset is null||!offsets.Contains(ambiguousOffset.Value))throw new ArgumentException("Ta godzina występuje dwukrotnie. Wybierz przesunięcie UTC.");
            return new(local,ambiguousOffset.Value);
        }
        return new(local,zone.GetUtcOffset(local));
    }
}
