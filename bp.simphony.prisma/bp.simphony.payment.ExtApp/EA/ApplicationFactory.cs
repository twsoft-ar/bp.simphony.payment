using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace TWS.Simphony.MP.Payment
{
    /// <summary>
    ///  Implements interface used by Simphony OPS to creates the application
    /// </summary>
    public class ApplicationFactory : Micros.PosCore.Extensibility.IExtensibilityAssemblyFactory
    {
        public Micros.PosCore.Extensibility.ExtensibilityAssemblyBase Create(Micros.PosCore.Extensibility.IExecutionContext context)
        {
            return new PrismaInterfaceExtApp(context);
        }

        public void Destroy(Micros.PosCore.Extensibility.ExtensibilityAssemblyBase app)
        {
            app.Destroy();
        }
    }
}
