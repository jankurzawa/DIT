using Dit.Core;
using Xunit;

namespace Dit.Tests;

public sealed class ProjectFormValidationTests
{
    private static ProjectFormInput Valid()
    {
        var root=Path.Combine(Path.GetTempPath(),"dit-form-"+Guid.NewGuid().ToString("N"));
        return new("Produkcja",Path.Combine(root,"main"),Path.Combine(root,"backup"),Path.Combine(root,"proxy"),Path.Combine(root,"premiere"),"Europe/Warsaw",
            "Kamera 1 | operator-password","Rejestrator 1 | 2","administrator-password","audio-password");
    }
    [Fact] public void Valid_form_allows_blank_optional_Adobe_settings()=>Assert.Empty(ProjectFormValidation.Validate(Valid()));
    [Fact] public void Empty_form_reports_all_required_fields()
    {
        var issues=ProjectFormValidation.Validate(new("","","","","","","","","",""));
        foreach(var field in new[]{ProjectField.Name,ProjectField.Main,ProjectField.Backup,ProjectField.Proxy,ProjectField.Premiere,ProjectField.TimeZone,ProjectField.Cameras})Assert.Contains(issues,i=>i.Field==field);
    }
    [Theory][InlineData("Kamera 1")][InlineData("Kamera 1 |")]
    public void Empty_passwords_are_allowed(string cameras)
    {
        Assert.Empty(ProjectFormValidation.Validate(Valid() with {AdminPassword="",AudioPassword="",Cameras=cameras}));
    }
    [Fact] public void Single_character_passwords_are_valid()=>Assert.Empty(ProjectFormValidation.Validate(Valid() with {AdminPassword="1",AudioPassword="2",Cameras="Kamera 1 | 3"}));
    [Fact] public void Editing_does_not_require_existing_passwords()=>Assert.Empty(ProjectFormValidation.Validate(Valid() with {Editing=true,ExistingCameraCount=1,Cameras="Kamera 1",AdminPassword="",AudioPassword=""}));
    [Fact] public void Added_camera_can_have_no_password_while_editing()=>Assert.Empty(ProjectFormValidation.Validate(Valid() with {Editing=true,ExistingCameraCount=1,Cameras="Kamera 1\nKamera 2"}));
    [Fact] public void Duplicate_device_names_mark_both_fields()
    {
        var issues=ProjectFormValidation.Validate(Valid() with {Audio="kamera 1 | 2"});
        Assert.Contains(issues,i=>i.Field==ProjectField.Cameras);Assert.Contains(issues,i=>i.Field==ProjectField.Audio);
    }
    [Fact] public void Nested_roots_mark_both_fields()
    {
        var form=Valid();var issues=ProjectFormValidation.Validate(form with {Backup=Path.Combine(form.Main,"backup")});
        Assert.Contains(issues,i=>i.Field==ProjectField.Main);Assert.Contains(issues,i=>i.Field==ProjectField.Backup);
    }
    [Fact] public void Relative_path_is_reported()=>Assert.Contains(ProjectFormValidation.Validate(Valid() with {Proxy="relative/proxy"}),i=>i.Field==ProjectField.Proxy);
    [Fact] public void Invalid_timezone_is_reported()=>Assert.Contains(ProjectFormValidation.Validate(Valid() with {TimeZone="unknown/timezone"}),i=>i.Field==ProjectField.TimeZone);
    [Theory] [InlineData("Audio | 0")] [InlineData("Audio | tekst")] [InlineData("Audio | -1")] [InlineData("Audio")]
    public void Malformed_audio_is_reported(string audio)=>Assert.Contains(ProjectFormValidation.Validate(Valid() with {Audio=audio}),i=>i.Field==ProjectField.Audio);
    [Fact] public void Spare_separator_from_old_form_is_accepted()=>Assert.Empty(ProjectFormValidation.Validate(Valid() with {Audio="Audio||20"}));
    [Fact] public void Optional_file_requires_correct_extension()
    {
        var form=Valid();Assert.Contains(ProjectFormValidation.Validate(form with {Template=Path.Combine(form.Premiere,"wrong.txt")}),i=>i.Field==ProjectField.Template);
    }
    [Fact] public void Audio_only_production_is_allowed()=>Assert.Empty(ProjectFormValidation.Validate(Valid() with {Cameras=""}));
    [Fact] public void Whitespace_required_value_is_missing()=>Assert.Contains(ProjectFormValidation.Validate(Valid() with {Name="   "}),i=>i.Field==ProjectField.Name);
}
