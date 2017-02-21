using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Extensions.AzureStorage.IntegrationTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Trace("Log this");
//            for (int i = 0; i < 100; i++)
//            {
//                logger.Trace("Trace Message: " + i);
//                logger.Debug("Debug Message: " + i);
//                logger.Info("Info Message: " + i);
//                logger.Warn("Warn Message: " + i);
//                logger.Error("Error Message: " + i);
//                logger.Fatal("Fata Message: " + i);
//                Thread.Sleep(10);
//            }
//
            try
            {
                throw new NotImplementedException();
            }
            catch (Exception ex)
            {
                logger.Error("We threw an exception", ex);
            }
            
            Console.ReadLine();
        }
    }
}
