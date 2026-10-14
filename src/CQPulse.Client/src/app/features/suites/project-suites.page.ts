import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [FormsModule, RouterLink, DataState],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">PROJECT WORKFLOW</p>
        <h1>{{ project.value()?.name ?? 'Test suites' }}</h1>
        <p class="intro">Suites group related test cases for this project.</p>
      </div>
      <a [routerLink]="['/projects', projectId()]">Project settings</a>
    </section>
    <app-data-state [loading]="project.loading()" [loaded]="project.loaded()" [error]="project.error()" (retry)="project.reload()" />
    <section class="split">
      <form class="panel form" (ngSubmit)="add()">
        <h2>New suite</h2>
        <label>Name<input [(ngModel)]="name" name="name" required maxlength="200"></label>
        <label>Description<textarea [(ngModel)]="description" name="description" maxlength="2000"></textarea></label>
        @if (saveError()) { <p class="error" role="alert">{{ saveError() }}</p> }
        <button class="primary" type="submit" [disabled]="saving() || !name.trim()">{{ saving() ? 'Creating…' : 'Create suite' }}</button>
      </form>
      <article class="panel">
        <div class="table-row heading"><span>Suite</span><span>Cases</span><span></span></div>
        @for (suite of suites.value()?.items ?? []; track suite.id) {
          <div class="table-row">
            <strong>{{ suite.name }}<small>{{ suite.description }}</small></strong>
            <span>{{ suite.testCaseCount }}</span>
            <a [routerLink]="['/suites', suite.id, 'cases']">Open →</a>
          </div>
        }
        <app-data-state [loading]="suites.loading()" [loaded]="suites.loaded()" [error]="suites.error()"
          [empty]="!suites.value()?.items?.length" emptyText="No suites yet." (retry)="suites.reload()" />
      </article>
    </section>`
})
export class ProjectSuitesPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly projectId = input.required<string>();
  protected readonly project = loadable(() => this.api.project(this.projectId()));
  protected readonly suites = loadable(() => this.api.suites(this.projectId(), { pageSize: 100 }));
  protected name = '';
  protected description = '';
  protected readonly saving = signal(false);
  protected readonly saveError = signal('');

  ngOnInit(): void {
    this.project.reload();
    this.suites.reload();
  }

  protected add(): void {
    this.saving.set(true);
    this.saveError.set('');
    this.api.saveSuite(this.projectId(), { name: this.name, description: this.description }).subscribe({
      next: () => { this.saving.set(false); this.name = ''; this.description = ''; this.suites.reload(); },
      error: e => { this.saving.set(false); this.saveError.set(problemMessage(e, 'Suite could not be created.')); }
    });
  }
}
