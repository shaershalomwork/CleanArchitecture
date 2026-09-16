import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { RouterModule, Routes } from '@angular/router';
import { LucideAngularModule, Users, PencilLine, CircleUserRound, Activity, LogIn, BookOpen, Menu } from 'lucide-angular';
import { AppComponent } from './app.component';
import { API_BASE_URL } from './web-api-client';
import { csrfInterceptor, sessionGuard } from './core/session.service';
import { CustomersComponent, CustomerEditorComponent } from './features/customers.component';
import { CustomerListComponent } from './features/customer-list.component';
import { AccountComponent, LandingComponent, SignInComponent, StatusComponent, UnavailableComponent } from './features/session.component';

const routes: Routes = [
  { path: 'sign-in', component: SignInComponent, title: 'Sign in · Customer services' },
  { path: '', component: LandingComponent, pathMatch: 'full', canActivate: [sessionGuard] },
  { path: 'customers', component: CustomerListComponent, canActivate: [sessionGuard], data: { permission: 'read' }, title: 'Customers · Customer services' },
  { path: 'customers/create', component: CustomerEditorComponent, canActivate: [sessionGuard], data: { permission: 'write', create: true }, title: 'Create customer · Customer services' },
  { path: 'customers/manage', component: CustomerEditorComponent, canActivate: [sessionGuard], data: { permission: 'write' }, title: 'Manage customers · Customer services' },
  { path: 'customers/:customerId/overview', component: CustomersComponent, canActivate: [sessionGuard], data: { permission: 'read' }, title: 'Customer overview · Customer services' },
  { path: 'account', component: AccountComponent, canActivate: [sessionGuard], title: 'Account · Customer services' },
  { path: 'status', component: StatusComponent, canActivate: [sessionGuard], title: 'Service status · Customer services' },
  { path: 'forbidden', component: UnavailableComponent, canActivate: [sessionGuard], data: { forbidden: true }, title: 'Access unavailable · Customer services' },
  { path: '**', component: UnavailableComponent, title: 'Page not found · Customer services' }
];
@NgModule({
  declarations: [AppComponent],
  imports: [BrowserModule, RouterModule.forRoot(routes, { scrollPositionRestoration: 'enabled' }), LucideAngularModule.pick({ Users, PencilLine, CircleUserRound, Activity, LogIn, BookOpen, Menu })],
  providers: [provideHttpClient(withInterceptors([csrfInterceptor])), { provide: API_BASE_URL, useValue: '' }],
  bootstrap: [AppComponent]
})
export class AppModule {}
