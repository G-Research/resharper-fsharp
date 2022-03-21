using LibWithTP;
 
namespace CSharpLib
{
    public static class Class
    {
        public static void Foo()
        {
            var config = new GeneratedConfig().Level2;
            var items = Config.instance.Level1.Level12.Level13;
            var item = items[0].age;  
            Config.instance.Changed += (sender, args) => { };
            
            Config.f(new GeneratedConfig.Level1_Type());

        }
    }
}
