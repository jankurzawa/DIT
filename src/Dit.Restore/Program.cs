using Dit.Infrastructure;

if(args.Length<2){Console.Error.WriteLine("Dit.Restore <manifest.json> <folder docelowy> [--7z <exe>] [--map <stary root> <nowy root>]...");return 2;}
var archive="7z";Dictionary<string,string> mappings=[];
for(var i=2;i<args.Length;i++)
{
    if(args[i]=="--7z"&&i+1<args.Length)archive=args[++i];
    else if(args[i]=="--map"&&i+2<args.Length){var old=args[++i];mappings[old]=args[++i];}
    else {Console.Error.WriteLine("Niepoprawne argumenty.");return 2;}
}
try{await OfflineRestore.Restore(args[0],args[1],mappings,new SevenZip(archive),default);Console.WriteLine("Odtworzono wszystkie pliki; SHA-256 zgodne.");return 0;}
catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
