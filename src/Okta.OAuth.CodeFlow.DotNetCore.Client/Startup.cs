
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;

namespace Okta.OAuth.CodeFlow.DotNetCore.Client
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                //OktaDev: added config.json reference and config.json file
                //.AddJsonFile("config.json")
                .AddJsonFile($"config.{env.EnvironmentName}.json", optional: true)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            //OktaDev: add authentication services.
            services.AddAuthentication(sharedOptions => sharedOptions.SignInScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            //Add the console logger
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));

            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();


            //OktaDev: Configure the OWIN pipeline to use cookie auth.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AutomaticAuthenticate = true,
                CookieName = "MyApp",
                AuthenticationScheme = "Cookies",
                CookieSecure = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest
            });

            OpenIdConnectOptions oidcOptions = new OpenIdConnectOptions
            {
                Authority = Configuration["Okta:OrganizationUri"],
                ClientId = Configuration["Okta:ClientId"],
                //OktaDev: you can include the response type using Microsoft's library constants 
                //ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.CodeIdToken,
                ResponseType = Configuration["Okta:ResponseType"],
                AuthenticationScheme = "oidc",
                CallbackPath = "callback", //OktaDev: The forward slash is implied. Note that if we don't set this value manually, the callback path is by default /signin-oidc

                //RefreshOnIssuerKeyNotFound is enabled by default and takes care of key rollover so no need to configure it here (Okta does perform key rollover unless configured otherwise)

                Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    OnRemoteFailure = OnAuthenticationFailed,
                },
                
                
            };

            //OktaDev: add the OIDC default scopes + the custom Okta "groups" scope (that returns the user's groups filtered or not depending on your Okta OIDC client configuration)
            oidcOptions.Scope.Add(Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectScope.OpenIdProfile);
            oidcOptions.Scope.Add("email");
            oidcOptions.Scope.Add("phone");
            oidcOptions.Scope.Add("address");
            oidcOptions.Scope.Add("groups");
            oidcOptions.Scope.Add("offline_access");

            //OktaDev:  Configure the OWIN pipeline to use OpenID Connect authentication.
            app.UseOpenIdConnectAuthentication(oidcOptions);


            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        // OktaDev: Handle sign-in errors differently than generic errors.
        private Task OnAuthenticationFailed(Microsoft.AspNetCore.Authentication.FailureContext context)
        {
            context.HandleResponse();
            context.Response.Redirect("/Home/Error?message=" + context.Failure.Message);
            return Task.FromResult(0);
        }
    }
}
