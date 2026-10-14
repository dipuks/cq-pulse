import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth';
import { ThemeService } from '../core/theme.service';

/** Authenticated application layout: sidebar navigation, top bar, and the routed page. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html'
})
export class Shell {
  protected readonly theme = inject(ThemeService);
  private readonly auth = inject(AuthService);
  protected signOut(): void { this.auth.signOut(); }
}
