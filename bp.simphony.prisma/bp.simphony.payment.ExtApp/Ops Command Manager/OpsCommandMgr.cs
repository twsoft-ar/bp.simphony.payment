using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Micros.Ops;

namespace TWS.Simphony.Helpers
{
    public class OpsCommandMgr
    {
        private static OpsContext mOpsContext = null;

        public static void SendOpsCommand(OpsContext opsContext_, OpsCommand cmd_, Func<bool> waitCondition_)
        {
            mOpsContext = opsContext_;
            new Thread(() => SendOpsCommandThread(new object[] { cmd_ }, waitCondition_)) { Name = "SEND OPS COMMAND THREAD" }.Start();
            /*
             * we use lambda notation because in .NETCF there is no ParametrizedThreadStart support.
             * This way we can pass any arguments in a similar way
             */
        }

        private static void SendOpsCommandThread(object[] args_, Func<bool> waitCondition_)
        {
            while (!waitCondition_())
                Thread.Sleep(10);

            if (mOpsContext != null)
                mOpsContext.InvokeOnCommandThread(SendOpsCommandCallback, args_);
        }

        private static object SendOpsCommandCallback(object[] data_)
        {
            OpsCommand opsCmd = (OpsCommand)data_[0];
            mOpsContext.ProcessCommand(opsCmd);
            return null;
        }
    }
}
