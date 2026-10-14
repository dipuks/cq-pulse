import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { SaveProject } from '../../core/models';
import { DataState } from '../../shared/data-state';

@Component({
  imports: [FormsModule, RouterLink, DataState],
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">PROJECT</p><h1>{{ projectId() ? 'Edit project' : 'New project' }}</h1></div>
      <a routerLink="/projects">← Projects</a>
    </section>
    <app-data-state [loading]="loading()" [loaded]="!loading()" [error]="loadError()" (retry)="load()" />
    @if (!loading() && !loadError()) {
      <form class="panel form" (ngSubmit)="save()">
        <label>Name<input [(ngModel)]="model.name" name="name" required maxlength="200"></label>
        <label>Description<textarea [(ngModel)]="model.description" name="description" maxlength="2000"></textarea></label>
        <label>Status
          <select [(ngModel)]="model.status" name="status"><option>Active</option><option>Archived</option></select>
        </label>
        @if (saveError()) { <p class="error" role="alert">{{ saveError() }}</p> }
        <button class="primary" type="submit" [disabled]="saving() || !model.name.trim()">{{ saving() ? 'Saving…' : 'Save project' }}</button>
        @if (projectId()) { <a [routerLink]="['/projects', projectId(), 'suites']">Manage suites →</a> }
      </form>
    }`
})
export class ProjectEditorPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  /** Bound from the :projectId route parameter; absent on /projects/new. */
  readonly projectId = input<string>();
  protected model: SaveProject = { name: '', description: '', status: 'Active' };
  protected readonly loading = signal(false);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly saveError = signal('');

  ngOnInit(): void { this.load(); }

  protected load(): void {
    const id = this.projectId();
    if (!id) return;
    this.loading.set(true);
    this.loadError.set('');
    this.api.project(id).subscribe({
      next: project => {
        this.model = { name: project.name, description: project.description ?? '', status: project.status };
        this.loading.set(false);
      },
      error: e => { this.loadError.set(problemMessage(e, 'Project could not be loaded.')); this.loading.set(false); }
    });
  }

  protected save(): void {
    this.saving.set(true);
    this.saveError.set('');
    this.api.saveProject(this.model, this.projectId()).subscribe({
      next: project => void this.router.navigate(['/projects', project.id, 'suites']),
      error: e => { this.saving.set(false); this.saveError.set(problemMessage(e, 'Project could not be saved.')); }
    });
  }
}
