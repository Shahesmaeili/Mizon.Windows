using System;
using System.Threading.Tasks;
using Windows.UI.StartScreen;

namespace Mizon.Windows.Services;

public class JumpListService
{
    public async Task ConfigureLoginRegisterJumpListAsync()
    {
        
        if (!JumpList.IsSupported()) return;

        var jumpList = await JumpList.LoadCurrentAsync();
        jumpList.Items.Clear();

        var signInJumpListItem = JumpListItem.CreateWithArguments("action=open-settings", "ورود به حساب کاربری");
        signInJumpListItem.Description = "ورود به میزون";
        signInJumpListItem.GroupName = "عملیات ها";
        signInJumpListItem.Logo = new Uri("ms-appx:///Assets/photo.jpg");

        var signUpJumpListItem = JumpListItem.CreateWithArguments("action=open-settings", "ساخت حساب کاربری");
        signUpJumpListItem.Description = "ساخت حساب در میزون";
        signUpJumpListItem.GroupName = "عملیات ها";
        signUpJumpListItem.Logo = new Uri("ms-appx:///Assets/photo.jpg");

        jumpList.Items.Add(signInJumpListItem);
        jumpList.Items.Add(signUpJumpListItem);

        await jumpList.SaveAsync();
    }
}
