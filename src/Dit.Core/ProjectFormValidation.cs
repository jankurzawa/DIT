namespace Dit.Core;

public enum ProjectField { Name,Main,Backup,Proxy,Premiere,TimeZone,Cameras,Audio,AdminPassword,AudioPassword,Template,SequencePreset,ProxyPreset,General }
public sealed record ProjectFieldIssue(ProjectField Field,string Message);
public sealed record ProjectFormInput(string Name,string Main,string Backup,string Proxy,string Premiere,string TimeZone,
    string Cameras,string Audio,string AdminPassword,string AudioPassword,bool Editing=false,int ExistingCameraCount=0,
    string Template="",string SequencePreset="",string ProxyPreset="");

public static class ProjectFormValidation
{
    public static string[] Lines(string text)=>text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);

    public static ProjectFieldIssue[] Validate(ProjectFormInput input)
    {
        List<ProjectFieldIssue> issues=[];
        void Add(ProjectField field,string message)=>issues.Add(new(field,message));
        void Name(ProjectField field,string value,string description)
        {
            if(string.IsNullOrWhiteSpace(value)){Add(field,"Uzupełnij "+description+".");return;}
            try{Paths.SafeName(value.Trim());}catch(ArgumentException){Add(field,"Popraw "+description+" — nie używaj znaków niedozwolonych w nazwach folderów.");}
        }
        bool Absolute(ProjectField field,string path,string description)
        {
            try
            {
                if(!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||path.IndexOfAny("<>\"|?*".ToCharArray())>=0||path.Any(char.IsControl))throw new ArgumentException();
                _=Path.GetFullPath(path);return true;
            }
            catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException)
            {Add(field,description+": podaj pełną lokalną ścieżkę, np. E:\\Produkcja.");return false;}
        }
        Name(ProjectField.Name,input.Name,"nazwę projektu");
        List<(ProjectField Field,string Path)> roots=[];
        void Roots(ProjectField field,string text,string description,bool list)
        {
            var paths=list?Lines(text):string.IsNullOrWhiteSpace(text)?[]:new[]{text.Trim()};
            if(paths.Length==0){Add(field,"Wskaż "+description+".");return;}
            foreach(var path in paths)if(Absolute(field,path,description))roots.Add((field,path));
        }
        Roots(ProjectField.Main,input.Main,"folder główny",true);Roots(ProjectField.Backup,input.Backup,"folder backupu",true);
        Roots(ProjectField.Proxy,input.Proxy,"folder proxy",false);Roots(ProjectField.Premiere,input.Premiere,"folder projektów Premiere",false);
        for(var i=0;i<roots.Count;i++)for(var j=i+1;j<roots.Count;j++)
            if(Configuration.IsWithin(roots[i].Path,roots[j].Path)||Configuration.IsWithin(roots[j].Path,roots[i].Path))
            {Add(roots[i].Field,"Lokalizacje nie mogą być takie same ani znajdować się jedna wewnątrz drugiej.");Add(roots[j].Field,"Lokalizacje nie mogą być takie same ani znajdować się jedna wewnątrz drugiej.");}
        if(string.IsNullOrWhiteSpace(input.TimeZone))Add(ProjectField.TimeZone,"Uzupełnij strefę czasową, np. Europe/Warsaw.");
        else try{_=TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone.Trim());}
            catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException){Add(ProjectField.TimeZone,"Nie rozpoznano strefy czasowej. Przykład: Europe/Warsaw.");}
        List<(ProjectField Field,string Name)> devices=[];
        var cameras=Lines(input.Cameras);var audio=Lines(input.Audio);
        if(cameras.Length==0&&audio.Length==0)Add(ProjectField.Cameras,"Dodaj przynajmniej jedną kamerę/operatora lub źródło audio.");
        for(var i=0;i<cameras.Length;i++)
        {
            var parts=cameras[i].Split('|',StringSplitOptions.TrimEntries);
            if(parts.Length>2){Add(ProjectField.Cameras,$"Wiersz {i+1}: podaj nazwę, opcjonalnie | hasło.");continue;}
            Name(ProjectField.Cameras,parts[0],$"nazwę kamery/operatora w wierszu {i+1}");devices.Add((ProjectField.Cameras,parts[0]));
        }
        for(var i=0;i<audio.Length;i++)
        {
            var parts=AudioParts(audio[i]);
            if(parts.Length!=2){Add(ProjectField.Audio,$"Wiersz {i+1}: podaj nazwę | liczbę kanałów, np. Audio | 20.");continue;}
            Name(ProjectField.Audio,parts[0],$"nazwę źródła audio w wierszu {i+1}");devices.Add((ProjectField.Audio,parts[0]));
            if(!int.TryParse(parts[1],out var channels)||channels<1)Add(ProjectField.Audio,$"Wiersz {i+1}: liczba kanałów musi być dodatnią liczbą całkowitą.");
        }
        foreach(var duplicate in devices.Where(d=>!string.IsNullOrWhiteSpace(d.Name)).GroupBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
            foreach(var field in duplicate.Select(d=>d.Field).Distinct())Add(field,"Nazwy kamer i źródeł audio muszą być unikalne.");
        void OptionalFile(ProjectField field,string path,string extension,string description)
        {
            if(string.IsNullOrWhiteSpace(path))return;
            if(Absolute(field,path.Trim(),description)&&!Path.GetExtension(path.Trim()).Equals(extension,StringComparison.OrdinalIgnoreCase))Add(field,description+": wybierz plik "+extension+".");
        }
        OptionalFile(ProjectField.Template,input.Template,".prproj","Projekt wzorcowy");OptionalFile(ProjectField.SequencePreset,input.SequencePreset,".sqpreset","Preset sekwencji");
        if(!BundledPresets.IsDefault(input.ProxyPreset))OptionalFile(ProjectField.ProxyPreset,input.ProxyPreset,".epr","Preset proxy");
        return issues.Distinct().ToArray();
    }
    public static string[] AudioParts(string line)
    {
        var parts=line.Split('|',StringSplitOptions.TrimEntries);
        // Accept a spare separator left over from the previous form: Audio || 20.
        return parts.Length==3&&parts[1].Length==0?[parts[0],parts[2]]:parts;
    }
}
