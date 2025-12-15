using CommunityToolkit.Mvvm.Messaging.Messages;
using MBDManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBDManager.Messages
{
    public class NewInspectionMessage : ValueChangedMessage<InspectionRecord>
    {
        public NewInspectionMessage(InspectionRecord value) : base(value)
        {
        }
    }
}
