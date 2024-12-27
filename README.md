<p align="right">
  <a href="https://ko-fi.com/U7U317ZN2L"><img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="ko-fi"></a>
</p>

## voxta-providers

# Installing Providers

1. Place the provider into the *Voxta.SampleProviderApp\Providers* folder
2. Place the respective config into *Voxta.SampleProviderApp\Providers\configs* folder
3. Add each Provider to the **Program.cs** inside the *Voxta.SampleProviderApp* folder

# Spotify Provider Setup

1. Set Up Your Spotify App: Go to the Spotify Developer Dashboard: https://developer.spotify.com/dashboard
2. Create an app to get your: Client ID, Client Secret, Set a Redirect URI (for OAuth2 authentication)
3. Install the provider
4. Add those values into the *Voxta.SampleProviderApp\Providers\configs\UserFunctionProviderSpotifyConfig.json*

# Weather API Provider Setup

For this provider to work we rely on a free external API service: https://openweathermap.org/. They have a free plan which is rate limited to max 60 calls per minute

1. Open the URL and register
2. Once you are registered and signed in, go to your profile and click on "My API keys"
3. Give your key a custom name and hit Generate (It can take a while till the API key is activated, check your emails)
4. Install the provider
5. Add the API key into the *Voxta.SampleProviderApp\Providers\configs\UserFunctionProviderWeatherConfig.json*

# Philips Hue Provider (light control) Setup

1. Install the provider
2. Follow the 'Hue Bridge' discovery described in the provider terminal after first launch
3. (Optional) Add a target light, zone or room name the character can control on it's own: *Voxta.SampleProviderApp\Providers\configs\UserFunctionProviderHueConfig.json*
