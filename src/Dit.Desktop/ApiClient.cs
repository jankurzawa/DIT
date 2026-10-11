using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dit.Desktop;

public sealed class ApiClient
{
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) {WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private readonly HttpClient http=new() {BaseAddress=new Uri("http://127.0.0.1:47831"),Timeout=TimeSpan.FromSeconds(30)};
    private readonly string data;
    public ApiClient(string data) {this.data=data;}
    public async Task<T> Get<T>(string path,string? token=null)=>await Send<T>(HttpMethod.Get,path,null,token);
    public async Task<T> Send<T>(HttpMethod method,string path,object? body=null,string? token=null,System.Threading.CancellationToken ct=default)
    {
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-DIT-Key",File.ReadAllText(Path.Combine(data,"local.key")).Trim());
        if(token is not null)request.Headers.Add("X-DIT-Session",token);
        if(body is not null)request.Content=JsonContent.Create(body,options:Json);
        using var response=await http.SendAsync(request,ct);
        var text=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode,text.Length==0?response.ReasonPhrase??"Błąd":text);
        return text.Length==0?default!:JsonSerializer.Deserialize<T>(text,Json)!;
    }
    public Task Post(string path,object? body=null,string? token=null)=>Send<JsonElement>(HttpMethod.Post,path,body,token);
}
public sealed class ApiException(int status,string message):Exception(Friendly(message))
{
    public int Status {get;}=status;
    private static string Friendly(string text){try{using var json=JsonDocument.Parse(text);return json.RootElement.TryGetProperty("error",out var error)?error.GetString()??text:text;}catch(JsonException){return text;}}
}
