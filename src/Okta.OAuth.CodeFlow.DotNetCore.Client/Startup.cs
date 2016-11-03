
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Linq;
using IdentityModel.Client;
using System;
using System.Security.Claims;

namespace Okta.OAuth.CodeFlow.DotNetCore.Client
{
    public class Startup
    {
        string oidcClientId = string.Empty;
        string oidcClientSecret = string.Empty;
        string oidcAuthority = string.Empty;
        string oidcRedirectUri = string.Empty;
        string oidcResponseType = string.Empty;

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
                CookieName = "MyApp",
                AuthenticationScheme = "Cookies",
            });

            string oidcClientId = Configuration["Okta:ClientId"] as string;
            string oidcClientSecret = Configuration["Okta:ClientSecret"] as string;
            string oidcAuthority = Configuration["Okta:OrganizationUri"] as string;
            string oidcRedirectUri = Configuration["Okta:RedirectUri"] as string;
            string oidcResponseType = Configuration["Okta:ResponseType"] as string;



            OpenIdConnectOptions oidcOptions = new OpenIdConnectOptions
            {
                Authority = oidcAuthority,
                ClientId = oidcClientId,
                //OktaDev: alternatively you can include the response type using Microsoft's library constants 
                //in Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType,
                ResponseType = oidcResponseType,
                //AuthenticationScheme is using the default value, but if you change it to something custom, you must update the AccountController.Login method as well
                AuthenticationScheme = Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
                CallbackPath = "/Callback", //OktaDev: The forward slash is implied. Note that if we don't set this value manually, the callback path is by default /signin-oidc

                //RefreshOnIssuerKeyNotFound is enabled by default and takes care of key rollover so no need to configure it here (Okta does perform key rollover unless configured otherwise)


                TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                {
                    ValidAudience = oidcClientId
                },

                Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents
                {
                    OnRemoteFailure = OnAuthenticationFailed,
                    OnAuthorizationCodeReceived = ExchangeCodeWithBasicAuthentication
                },


                
                
            };

            //OktaDev: add the OIDC default scopes + the custom Okta "groups" scope (that returns the user's groups filtered or not depending on your Okta OIDC client configuration)
            //oidcOptions.Scope.Add(Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectScope.OpenIdProfile);
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

        // OktaDev: Handle code exchange with Okta. We must override this event because the Microsoft.AspNetCore.Authentication.OpenIdConnect middleware uses only the client_post_method to handle authentication with the Token endpoint while 
        private Task OnAuthenticationFailed(Microsoft.AspNetCore.Authentication.FailureContext context)
        {
            context.HandleResponse();
            context.Response.Redirect("/Home/Error?message=" + context.Failure.Message);
            return Task.FromResult(0);
        }


        // OktaDev: Handle sign-in errors differently than generic errors.
        private Task ExchangeCodeWithBasicAuthentication(Microsoft.AspNetCore.Authentication.OpenIdConnect.AuthorizationCodeReceivedContext ctx)
        {
            // use the code to get the access and refresh token
            var tokenClient = new TokenClient(
                oidcAuthority + Constants.TokenEndpoint,
                oidcClientId,
                oidcClientSecret, AuthenticationStyle.BasicAuthentication);

            var tokenResponse = tokenClient.RequestAuthorizationCodeAsync(ctx.TokenEndpointRequest.Code, ctx.TokenEndpointRequest.RedirectUri);


            if (tokenResponse.IsFaulted)
            {
                throw tokenResponse.Exception;
            }

            // use the access token to retrieve claims from userinfo
            var userInfoClient = new UserInfoClient(oidcAuthority + Constants.UserInfoEndpoint);

            var userInfoResponse = userInfoClient.GetAsync(tokenResponse.Result.AccessToken);

            //// create new identity
            var id = new ClaimsIdentity(ctx.Ticket.AuthenticationScheme);
            //adding the claims we get from the userinfo endpoint
            id.AddClaims(userInfoResponse.Result.Claims);

            //also adding the ID, Access and Refresh tokens to the user claims 
            id.AddClaim(new Claim("id_token", ctx.JwtSecurityToken.RawData));
            id.AddClaim(new Claim("access_token", tokenResponse.Result.AccessToken));
            if (tokenResponse.Result.AccessToken != null)
                id.AddClaim(new Claim("refresh_token", tokenResponse.Result.RefreshToken));

            id.AddClaim(new Claim("expires_at", DateTime.Now.AddSeconds(tokenResponse.Result.ExpiresIn).ToLocalTime().ToString()));

            //n.AuthenticationTicket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new ClaimsIdentity(id.Claims, n.AuthenticationTicket.Identity.AuthenticationType),
            //    n.AuthenticationTicket.Properties);

            //context.HandleResponse();
            //context.Response.Redirect("/Home/Error?message=" + context.Failure.Message);
            return Task.FromResult(0);
        }
    }
}
