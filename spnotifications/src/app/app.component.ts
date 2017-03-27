import { Component, ViewChild } from '@angular/core';

import { Platform, MenuController, Nav } from 'ionic-angular';

import { StatusBar } from '@ionic-native/status-bar';
import { SplashScreen } from '@ionic-native/splash-screen';

import { HelloIonicPage } from '../pages/hello-ionic/hello-ionic';
import { ListPage } from '../pages/list/list';

// added these
import { Push } from 'ionic-native';
import { Http, Headers, RequestOptions } from '@angular/http';

@Component({
  templateUrl: 'app.html'
})
export class MyApp {
  @ViewChild(Nav) nav: Nav;

  // make HelloIonicPage the root (or first) page
  rootPage: any = HelloIonicPage;
  pages: Array<{ title: string, component: any }>;

  constructor(
    public platform: Platform,
    public menu: MenuController,
    public statusBar: StatusBar,
    public splashScreen: SplashScreen,
    // added this
    private http: Http
  ) {
    this.initializeApp();

    // set our app's pages
    this.pages = [
      { title: 'Hello Ionic', component: HelloIonicPage },
      { title: 'My First List', component: ListPage }
    ];
  }

  initializeApp() {
    this.platform.ready().then(() => {
      // Okay, so the platform is ready and our plugins are available.
      // Here you can do any higher level native things you might need.
      this.statusBar.styleDefault();
      this.splashScreen.hide();

      // added this
      let push = Push.init({
        android: {
          senderID: 'nada'
        },
        ios: {
          alert: 'true',
          badge: true,
          sound: 'false'
        },
        windows: {}
      });

      // added this
      push.on('registration', (data) => {

        let body = JSON.stringify({
          name: data.registrationId
        });

        let headers = new Headers({
          'Content-Type': 'application/json'
        });

        let options = new RequestOptions({
          headers: headers
        });

        this.http.post("https://xxx.azurewebsites.net/api/register", body, options)
          .subscribe(data => {
          }, error => {
            alert(JSON.stringify(error.json()));
          });
      });

      // added this
      push.on('notification', (data) => {
        alert(data.message);
      })

    });
  }

  openPage(page) {
    // close the menu when clicking a link from the menu
    this.menu.close();
    // navigate to the new page if it is not the current page
    this.nav.setRoot(page.component);
  }
}
