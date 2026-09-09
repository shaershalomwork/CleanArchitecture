import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { provideHttpClient } from '@angular/common/http';
import { AppComponent } from './app.component';
import { API_BASE_URL } from './web-api-client';

@NgModule({
  declarations: [AppComponent],
  imports: [BrowserModule, FormsModule],
  providers: [provideHttpClient(), { provide: API_BASE_URL, useValue: '' }],
  bootstrap: [AppComponent]
})
export class AppModule {}
