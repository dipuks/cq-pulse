import { DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, DataState],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">PROJECT CATALOG</p>
        <h1>Projects</h1>
        <p class="intro">Create and maintain the quality workspaces used by suites and runs.</p>
      </div>
      <a class="primary" routerLink="/projects/new">+ New project</a>
    </section>
    <section class="panel">
      <label class="search-input">Search projects
        <input [(ngModel)]="search" (ngModelChange)="projects.reload()" placeholder="Name or keyword">
      </label>
      <div class="table-row heading"><span>Name</span><span>Status</span><span>Created</span><span></span></div>
      @for (project of projects.value()?.items ?? []; track project.id) {
        <div class="table-row">
          <strong>{{ project.name }}<small>{{ project.description || 'No description' }}</small></strong>
          <span class="status" [attr.data-status]="project.status">{{ project.status }}</span>
          <span>{{ project.createdAtUtc | date: 'mediumDate' }}</span>
          <span><a [routerLink]="['/projects', project.id]">Edit</a> · <a [routerLink]="['/projects', project.id, 'suites']">Suites</a></span>
        </div>
      }
      <app-data-state [loading]="projects.loading()" [loaded]="projects.loaded()" [error]="projects.error()"
        [empty]="!projects.value()?.items?.length" emptyText="No projects match this search." (retry)="projects.reload()" />
    </section>`
})
export class ProjectsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected search = '';
  protected readonly projects = loadable(() => this.api.projects({ search: this.search.trim() }));

  ngOnInit(): void { this.projects.reload(); }
}
