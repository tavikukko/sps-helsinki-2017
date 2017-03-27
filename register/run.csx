#r "Microsoft.Azure.NotificationHubs"
#r "Newtonsoft.Json"
#r "Microsoft.WindowsAzure.Configuration.dll"

using System;
using System.Net;
using Microsoft.Azure;
using Microsoft.Azure.NotificationHubs;
using Newtonsoft.Json;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    string name = null;

    dynamic data = await req.Content.ReadAsAsync<object>();
    
    name = data?.name;

    if ( name == null ) return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name the request body");

    // CONNECT TO NOTIFICATION HUB
    NotificationHubClient Hub = NotificationHubClient.CreateClientFromConnectionString(Settings.NotificatioHubConnStr, Settings.NotificatioHubName);
         
    string newRegistrationId = null;

    var registrations = await Hub.GetRegistrationsByChannelAsync(name, 100);

    // get reg id from hub if exists
    foreach (RegistrationDescription reg in registrations)
    {
        if (newRegistrationId == null)
        {
            newRegistrationId = reg.RegistrationId;
        }
        else
        {
            await Hub.DeleteRegistrationAsync(reg);
        }
    }

    // create new if it doesnt
     if (newRegistrationId == null) 
         newRegistrationId = await Hub.CreateRegistrationIdAsync();

     RegistrationDescription registration = new AppleRegistrationDescription(name);
     registration.RegistrationId = newRegistrationId;

    // REGISTER DEVICE
    try
     {
         await Hub.CreateOrUpdateRegistrationAsync(registration);
     }
     catch (Exception e)
     {
         return req.CreateResponse(HttpStatusCode.BadRequest, "Error registering device: " + e);
     }

    log.Info("Device registered successfully to Notification Hub");
    return req.CreateResponse(HttpStatusCode.OK);
}

public class Settings
{
    public static readonly string NotificatioHubConnStr;
    public static readonly string NotificatioHubName;

    static Settings()
    {
        NotificatioHubConnStr = CloudConfigurationManager.GetSetting("NotificatioHubConnStr");
        NotificatioHubName = CloudConfigurationManager.GetSetting("NotificatioHubName");
    }
}