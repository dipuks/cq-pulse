import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/theme.service';

/** Root component. Layout is chosen by the route: Shell for authenticated pages, bare for /login. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html'
})
export class App implements OnInit {
  private readonly theme = inject(ThemeService);
  ngOnInit(): void { this.theme.initialize(); }
}
