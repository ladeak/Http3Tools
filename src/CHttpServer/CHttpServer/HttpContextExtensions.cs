using Microsoft.AspNetCore.Http;

namespace CHttpServer;

public static class HttpContextExtensions
{
    extension(HttpContext context)
    {
        public Priority9218 Priority() => context.Features.Get<IPriority9218Feature>()?.Priority ?? default;

        public void SetPriority(Priority9218 serverPriority) => context.Features.Get<IPriority9218Feature>()?.SetPriority(serverPriority);
    }
}