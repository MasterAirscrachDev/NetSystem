using System;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetSystem
{
    class Program
    {
        async static Task Main(string[] args)
        {
            //Testing t = new Testing();
            //await t.DoTests();
            TestingTwo t2 = new TestingTwo();
            await t2.DoTests();
            await Task.Delay(-1);
        }
    }   
}