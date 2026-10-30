import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { AppComponent } from './app/app.component';
import { tabSessionInterceptor } from './app/tab-session.interceptor';
bootstrapApplication(AppComponent, { providers: [provideRouter([]), provideHttpClient(withInterceptors([tabSessionInterceptor]))] }).catch(err => console.error(err));
