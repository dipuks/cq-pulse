import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, ThemeService } from './core';

/** Authenticated application layout: sidebar navigation, top bar, and the routed page. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html'
})
export class Shell {
  protected readonly dark = signal(false);
  constructor(private readonly theme: ThemeService, private readonly auth: AuthService) { this.dark.set(this.theme.isDark()); }
  protected toggleTheme(): void { this.dark.set(this.theme.set(!this.dark())); }
  protected signOut(): void { this.auth.signOut(); }
}
