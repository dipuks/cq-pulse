import { Component, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core';

/** Root component. Layout is chosen by the route: Shell for authenticated pages, bare for /login. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html'
})
export class App implements OnInit {
  constructor(private readonly theme: ThemeService) {}
  ngOnInit(): void { this.theme.initialize(); }
}
