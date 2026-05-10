using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AtlasAI.Modules.Email.Models;

namespace AtlasAI.Modules.Email.Services;

public interface IEmailProviderService
{
	Task<EmailMessageDetail> GetMessageAsync(string accountId, string messageId, CancellationToken cancellationToken);
}
