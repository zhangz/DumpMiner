﻿using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DumpMiner.Common;
using DumpMiner.Debugger;
using DumpMiner.Models;

namespace DumpMiner.Operations
{
    [Export(OperationNames.DumpModules, typeof(IDebuggerOperation))]
    class DumpModulesOperation : IDebuggerOperation
    {
        public string Name => OperationNames.DumpModules;

        public async Task<IEnumerable<object>> Execute(OperationModel model, CancellationToken token, object customeParameter)
        {
            List<string> types = model.Types?.Split(';').ToList();
            return await DebuggerSession.Instance.ExecuteOperation(() =>
            {
                var enumerable = from appDomain in DebuggerSession.Instance.Runtime.AppDomains
                                 from module in appDomain.Modules
                                 let name = module.Name
                                 where !string.IsNullOrEmpty(name) && (types == null || types.Any(t => name.ToLower().Contains(t.ToLower())))
                                 select new
                                 {
                                     Identifier = module.AssemblyId,
                                     Name = name.Substring(name.LastIndexOf('\\') + 1),
                                     FilePath = name.Substring(0, name.LastIndexOf('\\')),
                                     Size = module.Size,
                                     IsDynamic = module.IsDynamic
                                 };
                return enumerable.ToList();
            });
        }
    }
}