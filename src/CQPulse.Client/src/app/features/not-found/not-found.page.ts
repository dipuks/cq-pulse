import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  imports: [RouterLink],
  template: `
    <main class="login-page">
      <section class="section-banner">
        <p class="eyebrow">404</p>
        <h1>Page not found</h1>
        <p class="intro">This route does not exist in the CQ Pulse workspace.</p>
        <a class="primary" routerLink="/dashboard">Go to dashboard</a>
      </section>
    </main>`
})
export class NotFoundPage {}
