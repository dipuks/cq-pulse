import { DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ProjectStatus } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { Pager } from '../../shared/pager';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, DataState, Pager],
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
      <div class="filters">
        <label>Search<input [(ngModel)]="search" (ngModelChange)="applyFilters()" placeholder="Name or keyword"></label>
        <label>Status
          <select [(ngModel)]="status" (ngModelChange)="applyFilters()">
            <option value="">All</option><option value="Active">Active</option><option value="Archived">Archived</option>
          </select>
        </label>
        <label>Sort
          <select [(ngModel)]="sort" (ngModelChange)="applyFilters()">
            <option value="name">Name A–Z</option><option value="name-desc">Name Z–A</option>
            <option value="created-desc">Newest first</option><option value="created-asc">Oldest first</option>
          </select>
        </label>
      </div>
      <div class="table-row heading"><span>Name</span><span>Status</span><span>Created</span><span></span></div>
      @for (project of projects.value()?.items ?? []; track project.id) {
        <div class="table-row">
          <strong><a [routerLink]="['/projects', project.id]">{{ project.name }}</a><small>{{ project.description || 'No description' }}</small></strong>
          <span class="status" [attr.data-status]="project.status">{{ project.status }}</span>
          <span>{{ project.createdAtUtc | date: 'mediumDate' }}</span>
          <span><a [routerLink]="['/projects', project.id]">Details</a> · <a [routerLink]="['/projects', project.id, 'suites']">Suites</a></span>
        </div>
      }
      <app-data-state [loading]="projects.loading()" [loaded]="projects.loaded()" [error]="projects.error()"
        [empty]="!projects.value()?.items?.length" emptyText="No projects match these filters." (retry)="projects.reload()" />
      <app-pager [page]="page" [pageSize]="pageSize" [totalCount]="projects.value()?.totalCount ?? 0" (pageChange)="goTo($event)" />
    </section>`
})
export class ProjectsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly pageSize = 25;
  protected search = '';
  protected status: ProjectStatus | '' = '';
  protected sort = 'name';
  protected page = 1;
  protected readonly projects = loadable(() =>
    this.api.projects({ search: this.search.trim(), status: this.status, sort: this.sort, page: this.page, pageSize: this.pageSize }));

  ngOnInit(): void { this.projects.reload(); }

  protected applyFilters(): void { this.page = 1; this.projects.reload(); }
  protected goTo(page: number): void { this.page = page; this.projects.reload(); }
}
