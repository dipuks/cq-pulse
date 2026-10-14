import { Component, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ThemeService, AuthService } from './core';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html'
})
export class App implements OnInit {
  protected readonly dark = signal(false);
  constructor(private readonly theme: ThemeService, private readonly auth: AuthService) {}
  ngOnInit(): void { this.dark.set(this.theme.initialize()); }
  protected toggleTheme(): void { this.dark.set(this.theme.set(!this.dark())); }
  protected signOut(): void { this.auth.signOut(); }
}
